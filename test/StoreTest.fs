/// A component reading a store.
///
/// The point of a store is that more than one component reads it and none of them owns
/// it, so the test uses two: change the value once, and both should be showing it
/// afterwards without either having been told about the other.
module StoreTest

open System
open Browser
open Elmish
open Fable
open Lit
open Lit.Elmish
open LitStore
open WebTestRunner

let private counter: IStore<int> = Store.make (fun () -> 1) ignore ()

[<HookComponent>]
let private Shown () =
    let value = Hook.useStore counter
    html $"""<b class="value">{value}</b>"""

[<LitElement("store-first")>]
let First () =
    LitElement.init (fun config -> config.useShadowDom <- false) |> ignore
    Shown()

[<LitElement("store-second")>]
let Second () =
    LitElement.init (fun config -> config.useShadowDom <- false) |> ignore
    Shown()

/// The same hook, called by the element itself rather than by a hook component inside it.
/// Two different hook contexts and two different paths back from a disconnection, so both
/// are worth a test.
[<LitElement("store-direct")>]
let Direct () =
    LitElement.init (fun config -> config.useShadowDom <- false) |> ignore
    let value = Hook.useStore counter
    html $"""<b class="value">{value}</b>"""

describe "Store" <| fun () ->

    it "renders the current value, and every reader sees a change" <| fun () -> promise {
        let host = document.createElement "div"
        document.body.appendChild host |> ignore
        host.innerHTML <- "<store-first></store-first><store-second></store-second>"

        do! Promise.sleep 100

        let shown () =
            let found = host.querySelectorAll ".value"
            [ for i in 0 .. int found.length - 1 -> found.[i].textContent ]

        if shown () <> [ "1"; "1" ] then
            failwith $"""the components did not start from the store ({shown ()})"""

        counter.Update(fun current -> current + 41)
        do! Promise.sleep 100

        if shown () <> [ "42"; "42" ] then
            failwith $"""a change reached {shown ()} rather than both readers"""

        document.body.removeChild host |> ignore
    }

    // Disconnecting is not the end of a component: an element that is moved is
    // disconnected and connected again with its state kept. A subscription disposed on
    // the way out and not taken again leaves the second life of the component showing
    // the value the first one ended on -- which looks like a rendering bug and is not.
    // Both are run through the same steps: the hook is the same either way, but the
    // route a disconnection takes to reach it is not. An element that calls `useStore`
    // itself is told by its own connected and disconnected callbacks; a hook component
    // inside one is told by lit, through the part its parent rendered it into.
    for tag in [ "store-first"; "store-direct" ] do

     it $"stops listening when disconnected, and catches up when connected again ({tag})" <| fun () -> promise {
        let host = document.createElement "div"
        document.body.appendChild host |> ignore

        // A reader that never goes away. `Fable.Store` disposes a store when its last
        // subscriber leaves, so without this the test would be measuring that instead:
        // the component would take the only subscription, drop it on the way out, and
        // find a dead store waiting when it came back.
        let keepAlive = counter |> Store.subscribeImmediate ignore |> snd

        host.innerHTML <- $"<{tag}></{tag}>"

        do! Promise.sleep 100

        let element = host.querySelector tag
        let shown () = (host.querySelector ".value").textContent

        counter.Update(fun _ -> 1)
        do! Promise.sleep 100

        if shown () <> "1" then
            failwith $"the component did not start from the store ({shown ()})"

        // Out of the document, and the store moves without it.
        host.removeChild element |> ignore
        do! Promise.sleep 50

        counter.Update(fun _ -> 7)
        do! Promise.sleep 100

        if element.querySelector(".value").textContent <> "1" then
            failwith "a disconnected component was still being rendered into by the store"

        // Back in: the value it missed should be the value it shows.
        host.appendChild element |> ignore
        do! Promise.sleep 100

        if shown () <> "7" then
            failwith $"reconnecting left the component on its old value ({shown ()})"

        // And it is listening again, rather than having caught up once.
        counter.Update(fun _ -> 8)
        do! Promise.sleep 100

        if shown () <> "8" then
            failwith $"the component caught up on reconnect but did not resubscribe ({shown ()})"

        keepAlive.Dispose()
        document.body.removeChild host |> ignore
     }

// The other way of holding state in a component: its own Elmish loop rather than a shared
// store. `useElmish` starts the program in the state initialiser, so the loop belongs to
// the instance and dies with it -- except that dying is exactly what this checks, because
// nothing in `useElmish` stops the program.
module private OwnLoop =
    type Msg = Tick

    let mutable stopped = false
    let mutable ticks = 0

    let private subscribe _ : Sub<Msg> =
        [ [ "ticker" ],
          fun _ ->
              { new IDisposable with
                  member _.Dispose() = stopped <- true } ]

    let program () =
        Program.mkHidden (fun () -> 0, Cmd.none) (fun Tick n -> ticks <- ticks + 1; n + 1, Cmd.none)
        |> Program.withSubscription subscribe

[<LitElement("elmish-own-loop")>]
let OwnLoopElement () =
    LitElement.init (fun config -> config.useShadowDom <- false) |> ignore
    let n, dispatch = Hook.useElmish OwnLoop.program
    html $"""<b class="value">{n}</b><button @click={Ev(fun _ -> dispatch OwnLoop.Tick)}>tick</button>"""

describe "A component with its own Elmish loop" <| fun () ->

    it "keeps running after the element leaves the document" <| fun () -> promise {
        let host = document.createElement "div"
        document.body.appendChild host |> ignore
        host.innerHTML <- "<elmish-own-loop></elmish-own-loop>"

        do! Promise.sleep 100

        let element = host.querySelector "elmish-own-loop"
        (element.querySelector "button" :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 100

        if OwnLoop.ticks <> 1 then
            failwith $"the loop did not run at all ({OwnLoop.ticks})"

        let before = OwnLoop.ticks

        // Out of the document, and then dispatched to anyway -- which is what a timer or
        // a socket that outlived the element would be doing.
        host.removeChild element |> ignore
        do! Promise.sleep 100

        (element.querySelector "button" :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 100

        // This is documentation, not an aspiration: `useElmish` disposes the *model* if
        // the model happens to be IDisposable, and nothing else. The program was started
        // with `Program.run` and there is nothing holding a handle to stop it, so its
        // subscriptions outlive the element that started them.
        if OwnLoop.stopped then
            failwith "useElmish stopped the program's subscriptions -- if this now happens, the note below is out of date"

        if OwnLoop.ticks = before then
            failwith "the disconnected component's loop refused a message, which it is not expected to"

        document.body.removeChild host |> ignore
    }

// Both at once: a component with its own loop that also reads shared state.
//
// The two hooks are independent and neither knows about the other. The store value is
// read on every render and never enters the local model, which is the point -- a copy in
// the model is a second answer to a question that already has one.
module private Mixed =
    type Msg = Bump

    let init () = 0, Cmd.none
    let update Bump n = n + 1, Cmd.none

    /// What an update cannot do is read the store, because it is `Msg -> Model -> ...`
    /// and the store is not in it. Partial application looks like the fix and is a trap:
    /// `useElmish` calls `program()` once, in the state initialiser, so an update built
    /// from a render's store value is built from the *first* render's value and keeps it
    /// forever. This one records what it was given so a test can say so.
    let mutable updateSawTheme = ""
    let updateWith (theme: string) Bump n =
        updateSawTheme <- theme
        n + 1, Cmd.none

[<LitElement("mixed-reader")>]
let MixedReader () =
    LitElement.init (fun config -> config.useShadowDom <- false) |> ignore
    let shared = Hook.useStore counter
    let own, dispatch = Hook.useElmish (Mixed.init, Mixed.update)

    html
        $"""<b class="shared">{shared}</b><b class="own">{own}</b>
            <button @click={Ev(fun _ -> dispatch Mixed.Bump)}>bump</button>"""

[<LitElement("mixed-stale")>]
let MixedStale () =
    LitElement.init (fun config -> config.useShadowDom <- false) |> ignore
    let shared = Hook.useStore counter
    // Deliberately the wrong way round, to pin the trap down.
    let own, dispatch = Hook.useElmish (Mixed.init, Mixed.updateWith (string shared))

    html $"""<b class="own">{own}</b><button @click={Ev(fun _ -> dispatch Mixed.Bump)}>bump</button>"""

describe "A component with both" <| fun () ->

    it "renders shared state and its own, and each moves without the other" <| fun () -> promise {
        let host = document.createElement "div"
        document.body.appendChild host |> ignore
        let keepAlive = counter |> Store.subscribeImmediate ignore |> snd

        counter.Update(fun _ -> 3)
        host.innerHTML <- "<mixed-reader></mixed-reader>"
        do! Promise.sleep 100

        let shared () = (host.querySelector ".shared").textContent
        let own () = (host.querySelector ".own").textContent

        if (shared (), own ()) <> ("3", "0") then
            failwith $"started at {(shared (), own ())} rather than (3, 0)"

        // The store moves: the shared half follows, the local model does not.
        counter.Update(fun _ -> 4)
        do! Promise.sleep 100

        if (shared (), own ()) <> ("4", "0") then
            failwith $"a store change left the component at {(shared (), own ())}"

        // The local loop moves: the other way round.
        (host.querySelector "button" :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 100

        if (shared (), own ()) <> ("4", "1") then
            failwith $"a local dispatch left the component at {(shared (), own ())}"

        keepAlive.Dispose()
        document.body.removeChild host |> ignore
    }

    // The trap, asserted rather than described: an update partially applied with a
    // rendered store value keeps the value it was built with, because the program is
    // built once and never again.
    it "cannot see later store values through a partially applied update" <| fun () -> promise {
        let host = document.createElement "div"
        document.body.appendChild host |> ignore
        let keepAlive = counter |> Store.subscribeImmediate ignore |> snd

        counter.Update(fun _ -> 100)
        host.innerHTML <- "<mixed-stale></mixed-stale>"
        do! Promise.sleep 100

        counter.Update(fun _ -> 200)
        do! Promise.sleep 100

        (host.querySelector "button" :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 100

        if Mixed.updateSawTheme <> "100" then
            failwith
                $"the update saw {Mixed.updateSawTheme}; if this is now 200 the program is being rebuilt per render and the warning in the docs is wrong"

        keepAlive.Dispose()
        document.body.removeChild host |> ignore
    }
