/// A component reading a store.
///
/// The point of a store is that more than one component reads it and none of them owns
/// it, so the test uses two: change the value once, and both should be showing it
/// afterwards without either having been told about the other.
module StoreTest

open Browser
open Fable
open Lit
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
