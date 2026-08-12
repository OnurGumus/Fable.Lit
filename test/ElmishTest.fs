/// What happens when the module that started an Elmish program runs a second time.
///
/// That is what a hot update is: the bundler hands the page a new copy of a module and
/// the top-level `Program.run` in it runs again, on an element the previous copy is
/// still driving. Nothing in these tests involves a bundler, because nothing in the
/// implementation does either -- a second mount on the same element is the whole of it,
/// and a test can simply mount twice.
module ElmishTest

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

let private init () = { SharedViews.Count = 0 }, Cmd.none

let private update (msg: SharedViews.Msg) (model: SharedViews.Model) =
    match msg with
    | SharedViews.Increment -> { model with SharedViews.Count = model.Count + 1 }, Cmd.none

describe "Elmish remount" <| fun () ->

    // The reason anyone wants a hot update rather than a reload: the state you are
    // looking at is usually the state you are working on, and reaching it again by hand
    // after every edit is most of what makes a reload expensive.
    it "carries the model over to a program mounted after it" <| fun () -> promise {
        let el = host ()

        Program.mkProgram init update SharedViews.counter
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50
        (el.querySelector "button" :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 50

        // The module running again: a second program, from the same init as the first.
        Program.mkProgram init update SharedViews.counter
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        let shown = (el.querySelector ".n").textContent

        if shown <> "1" then
            failwith $"the second mount started from init instead of the model on screen (shows {shown})"

        document.body.removeChild el |> ignore
    }

    // A replaced program that keeps running is not idle. Its subscriptions still fire,
    // and every message it has already handed out still reaches its setState, which
    // renders into the element the new program now owns.
    it "stops the program it replaced" <| fun () -> promise {
        let el = host ()
        let mutable stopped = false
        let mutable firstDispatch: SharedViews.Msg -> unit = ignore

        let view model dispatch =
            firstDispatch <- dispatch
            SharedViews.counter model dispatch

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
            failwith "the subscription was stopped before anything replaced it"

        Program.mkProgram init update SharedViews.counter
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        if not stopped then
            failwith "the replaced program's subscription is still running"

        let before = (el.querySelector ".n").textContent
        firstDispatch SharedViews.Increment
        do! Promise.sleep 50
        let after = (el.querySelector ".n").textContent

        if before <> after then
            failwith $"a message dispatched to the replaced program still reached the DOM ({before} to {after})"

        document.body.removeChild el |> ignore
    }

    // The line a Vite or webpack entry module adds to let the bundler drive this. A test
    // run has no dev server behind it, which is the case worth pinning down: `accept` is
    // reached through `import.meta.hot`, which is not there, and this sits on the first
    // line of an application's startup where a throw takes the whole page with it.
    it "accepting hot updates is harmless where nothing implements them" <| fun () -> promise { HMR.acceptSelf () }

    // Adoption is a one-time act, and the second mount is not the one it is for. lit
    // refuses outright to hydrate a container it is already rendering into -- "container
    // already contains a live render" -- so a mount that asked would fall back to
    // emptying the element and rebuilding it, which is a reload with extra steps.
    it "renders rather than adopts when it takes over DOM lit already owns" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let el = host ()
        el.innerHTML <- (expected?("counter#hydratable"): string)

        let warnings = ResizeArray()
        let realWarn = console?warn
        console?warn <- fun (message: obj) -> warnings.Add message

        Program.mkProgram init update SharedViews.counter
        |> Program.withLitHydratedOnElement el
        |> Program.run

        do! Promise.sleep 50
        let adopted = el.querySelector "button"

        // The module running again, over DOM that belongs to lit now, not the server.
        Program.mkProgram init update SharedViews.counter
        |> Program.withLitHydratedOnElement el
        |> Program.run

        do! Promise.sleep 50
        console?warn <- realWarn

        if warnings.Count > 0 then
            failwith $"the second mount tried to adopt DOM lit already owned ({warnings.Count} warning(s))"

        if not (obj.ReferenceEquals(adopted, el.querySelector "button")) then
            failwith "the second mount rebuilt the element instead of patching it"

        document.body.removeChild el |> ignore
    }
