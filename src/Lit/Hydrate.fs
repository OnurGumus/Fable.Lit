namespace Lit

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

/// Adopting server-rendered markup instead of replacing it.
///
/// Only markup carrying lit's hydration markers can be adopted, which on the .NET side
/// means `Lit.Server`'s `renderHydratable`. Ordinary server HTML cannot be: lit finds
/// its bindings again through those markers and has no way to guess where they were.
///
/// This module is opt-in and imports `@lit-labs/ssr-client`, which is an experimental
/// package. Nothing else in Fable.Lit depends on it.
[<RequireQualifiedAccess>]
module Hydrate =

    [<Import("hydrate", "@lit-labs/ssr-client")>]
    let private hydrateImpl (value: obj) (container: Node) (options: obj) : unit = jsNative

    /// Through the dynamic operator because the container may be a shadow root, which has
    /// innerHTML but is not an Element.
    let private empty (container: obj) = container?innerHTML <- ""

    /// Why lit would not adopt the markup, or null when it did.
    ///
    /// Not every refusal is thrown. A container with no root marker in it -- an empty
    /// element, or markup rendered without `renderHydratable` -- is reported by lit to the
    /// console and nowhere else, and hydrate returns as if it had worked, leaving the
    /// container as it found it and no part on it. Left alone that is a blank island and
    /// a message most people never see, so it is treated as what it is.
    let private refusal (container: Node) (template: TemplateResult) (options: obj) : obj =
        try
            hydrateImpl (box template) container options

            if isNull ((box container)?("_$litPart$")) then
                box "the container held no markers to adopt"
            else
                null
        with error ->
            box error

    /// Adopts the server's markup if it can, and renders over it if it cannot.
    ///
    /// The fallback is the whole point of this function existing rather than the import
    /// being used directly. `hydrate` throws when the template it is given does not
    /// match the markup that was rendered -- a different digest, a value of an
    /// unexpected shape -- and it throws part way through, leaving a container it has
    /// begun to wire and will never finish. Left uncaught in a browser that is a blank
    /// panel and a stack trace.
    ///
    /// Catching it costs a full render, which is what would have happened without
    /// hydration at all, so the worst case is the ordinary case. What it must not do is
    /// hide the reason: the error goes to the console, because a page that silently
    /// stopped adopting is a performance regression nobody will ever find.
    ///
    /// The template and the data must be the ones the server rendered. That is not a
    /// suggestion: a template that differs is the mismatch this catches, and data that
    /// differs is markup that hydrates cleanly and then shows the wrong thing.
    /// The container is anything lit can render into, which is wider than an element: a
    /// shadow root is a DocumentFragment, and markup that arrived as
    /// `<template shadowrootmode="open">` is adopted there rather than on its host.
    let adopt (container: #Node) (template: TemplateResult) =
        match refusal (container :> Node) template (box {|  |}) with
        | null -> ()
        | reason ->
            Browser.Dom.console.warn ("lit could not adopt the server markup; rendering instead.", reason)

            // Emptied first. lit's `render` inserts its part into a container rather than
            // replacing what is already there, so rendering over markup it has just
            // refused leaves both copies on the page: the server's, which nothing is
            // wired to, and the client's underneath it. The fallback exists to make a
            // mismatch harmless, and a page shown twice is not harmless.
            empty container
            Lit.render container template

    /// Lets every component adopt the shadow root the server drew for it.
    ///
    /// A component can arrive with its shadow DOM already in place: the server writes
    /// its view into the tag with Lit.Server's `toShadowRootNode`, and the HTML parser
    /// attaches it as it reads the page. Without this the component, once defined,
    /// renders into that root as if it were empty, and the page shows two copies -- the
    /// server's, which nothing is wired to, above the one that works.
    ///
    /// Call it once, at startup, before any component has had the chance to render.
    /// Where it sits among the imports does not matter, because nothing happens when
    /// this module is loaded: it is the call that switches adoption on, and Fable.Lit
    /// defines its elements a moment after their module has run, never during it.
    ///
    /// From then on a `[<LitElement>]` that finds server-rendered markup in a shadow root
    /// already on it adopts that markup on its first render, exactly as `adopt` does for
    /// a container, and renders normally afterwards. One that finds no root, or a root
    /// with only a stylesheet in it, is untouched. The contract is the same as
    /// everywhere else: the component must render, first time, the template and the data
    /// the server rendered from -- its initial state, its attributes, or a store filled
    /// from the page. If it cannot adopt it says so in the console, clears the root and
    /// renders, so the worst case is the ordinary case.
    ///
    /// The component's own `styles` are adopted as they always are, next to whatever
    /// stylesheet the server put in the root. Send one: it is what styles the component
    /// before any script has run, and `Lit.unsafeCSS` lets both sides use one string.
    ///
    /// This is Fable.Lit's own components adopting, not lit's
    /// `lit-element-hydrate-support` module, for two reasons that are both pinned down
    /// in test/LitHydrateSupportTest.fs. That module has to be imported ahead of lit,
    /// and Fable writes a side-effect import last. And once applied it stops lit-element
    /// telling a rendered tree that its element has left the page, so a hook component
    /// inside any element, adopted or not, is never torn down.
    ///
    /// A component inside a view adopts the same way, from a root written there with
    /// `Lit.shadowRoot`, and as soon as it is defined -- unless the view hands it a
    /// property. A property is not in the HTML, so Lit.Server marks such a component
    /// `defer-hydration`, lit's word for "not yet", and it waits: hydrating the view
    /// around it takes the mark off and sets the property in the same pass, and the
    /// component adopts with what it was handed. That waiting needs nothing switched
    /// on; a `[<LitElement>]` honours the mark wherever it finds it.
    let elements () =
        ElementAdoption.adopt <-
            Some(fun (template, root, options) ->
                match refusal (root :> Node) template options with
                | null -> ()
                | reason ->
                    let tag: string = root?host?localName

                    Browser.Dom.console.warn (
                        $"lit could not adopt the server markup in <{tag}>; rendering instead.",
                        reason
                    )

                    // The same emptying as above, for the same reason. It takes the
                    // server's stylesheet with it, which is why the component's own was
                    // adopted before any of this was tried.
                    empty root)
