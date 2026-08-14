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
