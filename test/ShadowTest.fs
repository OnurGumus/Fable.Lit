/// Markup that arrives inside a declarative shadow root, and is adopted there.
///
/// `<template shadowrootmode="open">` is attached by the HTML parser as it reads the
/// page, so an element can arrive with its shadow DOM already built rather than waiting
/// for script to build one. Nothing in lit's marker protocol mentions the light DOM:
/// bindings are found by walking comment nodes, and comments live in a shadow root like
/// anywhere else. These tests hold that reasoning against a browser, using the markup
/// the .NET renderer actually writes.
module ShadowTest

open Browser
open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Lit
open Lit.Elmish
open WebTestRunner

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

/// innerHTML has not attached declarative shadow roots since Chrome 124: it leaves the
/// templates inert, which would make these tests pass without ever attaching one.
[<Emit("$0.setHTMLUnsafe($1)")>]
let private setHTMLUnsafe (el: obj) (html: string) : unit = jsNative

[<Emit("getComputedStyle($0).color")>]
let private colourOf (el: obj) : string = jsNative

/// A host carrying the server's shadow markup, in the document.
let private hostWith (id: string) (markup: string) =
    let holder = document.createElement "div"
    document.body.appendChild holder |> ignore
    setHTMLUnsafe holder $"""<span id="{id}">{markup}</span>"""
    holder, holder.querySelector $"#{id}"

describe "Declarative shadow DOM" <| fun () ->

    it "adopts the markup the server wrote into the shadow root" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let holder, host = hostWith "adopted" (expected?("counter#shadow"): string)
        let root: Browser.Types.ShadowRoot = host?shadowRoot

        if isNull (box root) then
            failwith "the browser did not attach the declarative shadow root"

        let before = root.querySelector "button"
        let mutable clicks = 0

        Hydrate.adopt root (SharedViews.counter { SharedViews.Count = 0 } (fun _ -> clicks <- clicks + 1))

        do! Promise.sleep 50

        if not (obj.ReferenceEquals(before, root.querySelector "button")) then
            failwith "adopting replaced the shadow root's button instead of taking it over"

        (before :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 50

        if clicks <> 1 then
            failwith $"the click never reached the handler, so the event part was never created ({clicks})"

        document.body.removeChild holder |> ignore
    }

    // The stylesheet is written into the root ahead of the markers, which is the reason
    // to put anything in a shadow root at all. It is also the only evidence available
    // from script that the root is a real one and not a template the parser left alone.
    it "keeps the server's styles, and keeps them inside" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let holder, host = hostWith "styled" (expected?("counter#shadow"): string)
        let root: Browser.Types.ShadowRoot = host?shadowRoot

        let outside = document.createElement "p"
        outside.className <- "n"
        holder.appendChild outside |> ignore

        Hydrate.adopt root (SharedViews.counter { SharedViews.Count = 0 } ignore)
        do! Promise.sleep 50

        let inside = root.querySelector ".n"

        if colourOf inside <> "rgb(1, 2, 3)" then
            failwith $"the shadow root's own stylesheet did not apply ({colourOf inside})"

        if colourOf outside = "rgb(1, 2, 3)" then
            failwith "the shadow root's stylesheet escaped it, so nothing was encapsulated"

        document.body.removeChild holder |> ignore
    }

    // The same handover as anywhere else, in a container that is not an element.
    it "drives an Elmish program mounted on the shadow root" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let holder, host = hostWith "elmish-shadow" (expected?("counter#shadow"): string)
        let root: Browser.Types.ShadowRoot = host?shadowRoot
        let before = root.querySelector "button"

        let init () = { SharedViews.Count = 0 }, Cmd.none

        let update (msg: SharedViews.Msg) (model: SharedViews.Model) =
            match msg with
            | SharedViews.Increment -> { model with SharedViews.Count = model.Count + 1 }, Cmd.none

        Program.mkProgram init update SharedViews.counter
        |> Program.withLitHydratedInShadowRoot "elmish-shadow"
        |> Program.run

        do! Promise.sleep 50

        if not (obj.ReferenceEquals(before, root.querySelector "button")) then
            failwith "the program rebuilt the shadow root instead of adopting what was in it"

        (before :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 50

        let shown = (root.querySelector ".n").textContent

        if shown <> "1" then
            failwith $"dispatch did not reach the view inside the shadow root (shows {shown})"

        document.body.removeChild holder |> ignore
    }
