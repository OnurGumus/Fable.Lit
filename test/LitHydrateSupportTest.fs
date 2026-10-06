/// Why `Hydrate.elements` is not lit's own `lit-element-hydrate-support`.
///
/// lit ships a module that makes a LitElement adopt a server-rendered shadow root, and a
/// `[<LitElement>]` is a LitElement, so the obvious thing is to load it and be done.
/// Two things are in the way, and neither announces itself. Each is held here against a
/// browser, as a fact about somebody else's code: when one of these tests fails, that
/// fact has changed, and the note on `Hydrate.elements` is the thing to revisit.
///
/// Nothing on this page calls `Hydrate.elements`. This is lit's arrangement, alone.
module LitHydrateSupportTest

open Browser
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Lit
open WebTestRunner

// The way lit's documentation says to do it: load this before lit.
importSideEffects "@lit-labs/ssr-client/lit-element-hydrate-support.js"

[<Import("LitElement", "lit")>]
let private litElement: obj = jsNative

/// The patch gives LitElement an `observedAttributes` of its own, where it only
/// inherited one.
[<Emit("Object.prototype.hasOwnProperty.call($0, 'observedAttributes')")>]
let private isApplied (ctor: obj) : bool = jsNative

/// What lit-element would have done for itself had it been loaded second.
[<Emit("globalThis.litElementHydrateSupport({ LitElement: $0 })")>]
let private applyByHand (ctor: obj) : unit = jsNative

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

[<Emit("$0.setHTMLUnsafe($1)")>]
let private setHTMLUnsafe (el: obj) (html: string) : unit = jsNative

/// Everything said to console.warn while this is held.
[<Emit("(() => { const said = []; const original = console.warn; console.warn = (...args) => { said.push(args.map(String).join(' ')); original.apply(console, args); }; return { said, stop() { console.warn = original; } }; })()")>]
let private listenToWarnings () : obj = jsNative

// Read while this module is being evaluated, which is when it would have happened.
let private appliedByTheImport = isApplied litElement

// Applied here for the tests that are about what it does once it is in force. Still
// during evaluation, so ahead of any element: Fable.Lit defines them a moment later.
if not appliedByTheImport then
    applyByHand litElement

[<LitElement("lits-counter")>]
let LitsCounter () =
    LitElement.init () |> ignore
    let count, setCount = Hook.useState 0
    SharedViews.counter { SharedViews.Count = count } (fun _ -> setCount (count + 1))

let private tenancy = ResizeArray<string>()

[<HookComponent>]
let Tenant () =
    Hook.useEffectOnce (fun () ->
        tenancy.Add "in"
        Hook.createDisposable (fun () -> tenancy.Add "out"))

    html $"<i>tenant</i>"

[<LitElement("lits-landlord")>]
let Landlord () =
    LitElement.init () |> ignore
    html $"<p>{Tenant()}</p>"

describe "lit's own hydrate support" <| fun () ->

    // It has to be evaluated before lit-element, which looks for it once, as it loads.
    // That is import order, and Fable decides import order: named imports in the order
    // they are first used, side-effect imports after all of them. So the line at the top
    // of this file, which is the line lit's documentation asks for, does nothing -- and
    // says nothing, which is how a page ends up showing every component twice.
    it "does not take effect from an import, because Fable writes a side-effect import last" <| fun () -> promise {
        if appliedByTheImport then
            failwith
                "lit's hydrate support took effect from importSideEffects alone. Either Fable no longer writes side-effect imports last, or lit no longer has to be loaded second; one of the two reasons Hydrate.elements is not built on it has gone."
    }

    // Applied, it does what it says, and a Fable.Lit component is an ordinary LitElement
    // to it. Fable.Lit has a warning for a server-rendered root that nothing is going to
    // adopt; a page that has made lit's arrangement must not be given it.
    it "adopts for a Fable.Lit component once it is applied, and Fable.Lit keeps quiet" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let shadow: string = expected?("counter#shadow")

        let holder = document.createElement "div"
        setHTMLUnsafe holder $"<lits-counter>{shadow}</lits-counter>"
        let el: HTMLElement = holder?firstElementChild
        let root: ShadowRoot = el?shadowRoot
        let served = root.querySelector "button"

        let ear = listenToWarnings ()
        document.body.appendChild holder |> ignore
        do! (el :?> LitElement).updateComplete
        do! Promise.sleep 50
        ear?stop () |> ignore

        if not (obj.ReferenceEquals(served, root.querySelector "button")) || root.querySelectorAll(".counter").length <> 1 then
            failwith "lit's hydrate support did not adopt the server's markup for a Fable.Lit component"

        let said: string[] = ear?said

        if said |> Array.exists (fun line -> line.Contains "Hydrate.elements") then
            failwith $"Fable.Lit warned about a root that lit's own support was about to adopt: {said}"

        document.body.removeChild holder |> ignore
    }

    // The second reason, and the one that would not be found by looking at a page.
    //
    // lit-element tells a rendered tree that its element has left the document through
    // the root part its own render returned, which it keeps. The patched update renders
    // and keeps nothing. From then on no directive inside any element is told -- adopted
    // or not, this one was never server-rendered at all -- and a hook component is a
    // directive: what `useEffectOnce` set up is never torn down.
    //
    // This asserts the hole is there. It is the same in lit's main branch at the time of
    // writing, so it is expected to hold for a while; the day it fails is good news.
    it "stops telling a rendered tree that its element has left the page" <| fun () -> promise {
        tenancy.Clear()

        let holder = document.createElement "div"
        document.body.appendChild holder |> ignore
        holder.innerHTML <- "<lits-landlord></lits-landlord>"
        let el: HTMLElement = holder?firstElementChild

        do! (el :?> LitElement).updateComplete
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in" ] then
            failwith $"the effect ran {List.ofSeq tenancy} on arrival, not [in], so this proves nothing about leaving"

        holder.removeChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in" ] then
            failwith
                $"lit's hydrate support now tells a tree when its element leaves ({List.ofSeq tenancy}). That was the second reason Hydrate.elements is not built on it."

        document.body.removeChild holder |> ignore
    }
