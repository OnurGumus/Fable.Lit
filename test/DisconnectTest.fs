/// Telling a rendered tree that it has gone.
///
/// Components know when they leave the page: a LitElement gets disconnectedCallback, a
/// HookComponent disposes what `Hook.useEffectOnce` handed back. Markup rendered into a
/// plain element knows nothing, and an Elmish program driving it will go on running
/// subscriptions and timers long after the element is out of the document, because
/// nothing ever said otherwise.
///
/// lit's answer is the root part, which `render` returns and Fable.Lit used to discard.
module DisconnectTest

open System
open Browser
open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Lit
open Lit.Elmish
open WebTestRunner

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

let private host () =
    let el = document.createElement "div"
    document.body.appendChild el |> ignore
    el

describe "Disconnecting" <| fun () ->

    // lit's own `ref` is an AsyncDirective, so it is told, and tells the callback by
    // handing it nothing. That makes it a usable proof that the message arrives.
    it "reaches the directives in a tree that was rendered into a plain element" <| fun () -> promise {
        let el = host ()
        let mutable attached = false

        let part =
            Lit.renderPart
                el
                (html $"""<p {Lit.refCallback (fun (found: Browser.Types.Element option) -> attached <- found.IsSome)}>here</p>""")

        do! Promise.sleep 50

        if not attached then
            failwith "the ref never saw the element, so this proves nothing about disconnecting"

        part.setConnected false
        do! Promise.sleep 50

        if attached then
            failwith "the tree was told it is disconnected and its directives were not"

        document.body.removeChild el |> ignore
    }

    // The island case. Nothing about a div says when its program should stop.
    it "stops the Elmish program mounted on a container" <| fun () -> promise {
        let el = host ()
        let mutable stopped = false
        let mutable dispatchOut: SharedViews.Msg -> unit = ignore

        let view model dispatch =
            dispatchOut <- dispatch
            SharedViews.counter model dispatch

        let init () = { SharedViews.Count = 0 }, Cmd.none

        let update (msg: SharedViews.Msg) (model: SharedViews.Model) =
            match msg with
            | SharedViews.Increment -> { model with SharedViews.Count = model.Count + 1 }, Cmd.none

        let subscribe _ : Sub<SharedViews.Msg> =
            [ [ "ticker" ],
              fun _ ->
                  { new IDisposable with
                      member _.Dispose() = stopped <- true } ]

        Program.mkProgram init update view
        |> Program.withSubscription subscribe
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        if stopped then
            failwith "the subscription was stopped before anything asked for it"

        Program.stopOn el
        do! Promise.sleep 50

        if not stopped then
            failwith "stopping the island left its subscription running"

        // And the loop is closed, so a message handed out earlier cannot come back and
        // render into an element nobody is looking at any more.
        let before = (el.querySelector ".n").textContent
        dispatchOut SharedViews.Increment
        do! Promise.sleep 50

        if (el.querySelector ".n").textContent <> before then
            failwith "a stopped program still rendered when dispatched to"

        document.body.removeChild el |> ignore
    }

    // The same, for an island that adopted the server's markup rather than rendering
    // its own. Hydration leaves nothing to return, so the part is read off the container
    // where lit stores it -- this is what says that read found the real thing: a missing
    // part would take setConnected with it.
    it "stops a hydrated island, subscription and adopted tree alike" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let el = host ()
        el.innerHTML <- (expected?("counter#hydratable"): string)

        let before = el.querySelector "button"
        let mutable stopped = false

        let init () = { SharedViews.Count = 0 }, Cmd.none

        let update (msg: SharedViews.Msg) (model: SharedViews.Model) =
            match msg with
            | SharedViews.Increment -> { model with SharedViews.Count = model.Count + 1 }, Cmd.none

        let subscribe _ : Sub<SharedViews.Msg> =
            [ [ "ticker" ],
              fun _ ->
                  { new IDisposable with
                      member _.Dispose() = stopped <- true } ]

        Program.mkProgram init update SharedViews.counter
        |> Program.withSubscription subscribe
        |> Program.withLitHydratedOnElement el
        |> Program.run

        do! Promise.sleep 50

        if not (obj.ReferenceEquals(before, el.querySelector "button")) then
            failwith "the program rebuilt the markup instead of adopting it, so this is not the hydrated path"

        if stopped then
            failwith "the subscription was stopped before anything asked for it"

        Program.stopOn el
        do! Promise.sleep 50

        if not stopped then
            failwith "stopping a hydrated island left its subscription running"

        document.body.removeChild el |> ignore
    }

    // Where the automatic part ends, which is not where it looks like it ends.
    //
    // lit propagates disconnection through its own rendering: clear a part -- a
    // conditional going to Lit.nothing, a list item dropping out -- and every directive
    // in what it removed is told, with nobody asking. What it does not do is watch the
    // document. Take the container out by hand and lit learns nothing, because nothing
    // in it observes the DOM. LitElement bridges that for components by calling
    // setConnected from its own connected and disconnected callbacks; an island has no
    // callbacks to borrow.
    it "tells directives when lit clears them, and not when the DOM is torn out from under it" <| fun () -> promise {
        let el = host ()
        let mutable attached = false

        let marked =
            html $"""<p {Lit.refCallback (fun (found: Browser.Types.Element option) -> attached <- found.IsSome)}>here</p>"""

        // Rendered inside a hole, so the next render can take it away.
        Lit.render el (html $"""<div class="slot">{marked}</div>""")
        do! Promise.sleep 50

        if not attached then
            failwith "the ref never saw the element"

        // lit removes it: told.
        Lit.render el (html $"""<div class="slot">{Lit.nothing}</div>""")
        do! Promise.sleep 50

        if attached then
            failwith "lit cleared the part and the directive in it was never told"

        // Now the other half. Put it back, then remove the whole container the way any
        // code outside lit would.
        Lit.render el (html $"""<div class="slot">{marked}</div>""")
        do! Promise.sleep 50

        if not attached then
            failwith "the ref did not come back when lit rendered it again"

        document.body.removeChild el |> ignore
        do! Promise.sleep 50

        if not attached then
            failwith "lit noticed a plain DOM removal, which would mean this whole API is unnecessary"
    }

    // Borrowing the browser's own answer. A tag with a dash can be upgraded to a custom
    // element, and custom elements are the only things the browser tells about joining
    // and leaving the document -- so an island wrapped in one gets what a LitElement has,
    // without being a LitElement.
    it "tracks connection through a host element, both ways" <| fun () -> promise {
        Lit.trackConnection "island-host"

        let holder = host ()
        holder.innerHTML <- """<island-host id="tracked"></island-host>"""

        let tracked = holder.querySelector "#tracked"
        let mutable attached = false

        Lit.render
            tracked
            (html $"""<p {Lit.refCallback (fun (found: Browser.Types.Element option) -> attached <- found.IsSome)}>here</p>""")

        do! Promise.sleep 50

        if not attached then
            failwith "the ref never saw the element"

        // Out of the document: the host is told, and passes it on.
        holder.removeChild tracked |> ignore
        do! Promise.sleep 50

        if attached then
            failwith "the host left the document and what it rendered was never told"

        // Back in: an element that was moved should come back with everything it had.
        holder.appendChild tracked |> ignore
        do! Promise.sleep 50

        if not attached then
            failwith "the host came back and what it rendered stayed disconnected"

        document.body.removeChild holder |> ignore
    }
