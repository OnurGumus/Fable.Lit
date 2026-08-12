namespace Lit

open Browser.Types
open Fable.Core

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
    let adopt (container: Element) (template: TemplateResult) =
        try
            hydrateImpl (box template) (container :> Node) (box {|  |})
        with error ->
            Browser.Dom.console.warn ("lit could not adopt the server markup; rendering instead.", error)
            Lit.render container template
