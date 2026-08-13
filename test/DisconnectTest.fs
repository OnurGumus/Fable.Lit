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
open Fable.Core.JsInterop
open Elmish
open Lit
open Lit.Elmish
open WebTestRunner

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
