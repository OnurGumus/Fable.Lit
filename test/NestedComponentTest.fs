/// A component inside a view, drawn in advance like the view around it.
///
/// `toShadowRootNode` draws a component's root where a page template can hold it. A
/// component that sits inside an island's view, or inside another component's, has no
/// page template around it: what is around it is a view, and a view is the same code on
/// both sides. `Lit.shadowRoot` is how such a view says "and this is what that component
/// draws" -- markup on the server, nothing in the browser.
///
/// Three things have to hold, and each is a test below. The view around the component
/// adopts as before, with the component's root sitting in one of its holes. The
/// component adopts its own root, independently. And a component that the view hands a
/// property waits for the view, because until the view has hydrated it does not have
/// the property, and would draw something other than what the server drew.
module NestedComponentTest

open Browser
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Lit
open WebTestRunner

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

/// innerHTML has not attached declarative shadow roots since Chrome 124, see ShadowTest.
[<Emit("$0.setHTMLUnsafe($1)")>]
let private setHTMLUnsafe (el: obj) (html: string) : unit = jsNative

/// Everything said to console.warn or console.error while this is held.
[<Emit("(() => { const said = []; const originals = { warn: console.warn, error: console.error }; for (const level of ['warn', 'error']) { console[level] = (...args) => { said.push(args.map(String).join(' ')); originals[level].apply(console, args); }; } return { said, stop() { Object.assign(console, originals); } }; })()")>]
let private listenToComplaints () : obj = jsNative

Hydrate.elements ()

/// The component inside the views in SharedViews: a counter with a count of its own.
[<LitElement("nested-counter")>]
let NestedCounter () =
    LitElement.init () |> ignore
    let count, setCount = Hook.useState 0
    SharedViews.counter { SharedViews.Count = count } (fun _ -> setCount (count + 1))

/// Handed its name as a property and never as an attribute, so the name is not in the
/// markup the server sends. Until somebody sets it, this draws the other template.
[<LitElement("nested-greeting")>]
let NestedGreeting () =
    let _, props =
        LitElement.init (fun config -> config.props <- {| who = Prop.Of("", attribute = "") |})

    SharedViews.greeting props.who.Value

/// A component whose own view has a component in it.
[<LitElement("nested-outer")>]
let NestedOuter () =
    LitElement.init () |> ignore
    let count, setCount = Hook.useState 0
    SharedViews.host { SharedViews.Count = count } (fun _ -> setCount (count + 1))

/// What the component below set up and tore down, in order.
let private tenancy = ResizeArray<string>()

/// Something with an effect, to be told to wait and then moved before it has started.
[<LitElement("nested-waiter")>]
let NestedWaiter () =
    LitElement.init () |> ignore

    Hook.useEffectOnce (fun () ->
        tenancy.Add "in"
        Hook.createDisposable (fun () -> tenancy.Add "out"))

    html $"<i>waited</i>"

let private same (a: obj) (b: obj) = obj.ReferenceEquals(a, b)

let private updated (el: Element) = promise {
    do! (el :?> LitElement).updateComplete
    do! Promise.sleep 50
}

describe "A component inside a view" <| fun () ->

    it "is adopted in an island, and adopts its own root there" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let markup: string = expected?("host#hydratable")

        let holder = document.createElement "div"
        setHTMLUnsafe holder markup

        let inner = holder.querySelector "nested-counter"
        let innerRoot: ShadowRoot = inner?shadowRoot

        if isNull (box innerRoot) then
            failwith "the browser did not attach the root the server drew inside the view"

        let servedInner = innerRoot.querySelector "button"
        let servedOuter = holder.querySelector "button.outer"
        let mutable outerClicks = 0
        let view count = SharedViews.host { SharedViews.Count = count } (fun _ -> outerClicks <- outerClicks + 1)

        let ear = listenToComplaints ()
        document.body.appendChild holder |> ignore
        Hydrate.adopt holder (view 0)
        do! updated inner
        ear?stop () |> ignore

        let said: string[] = ear?said

        if said.Length <> 0 then
            failwith $"adopting a view with a component in it had something to complain about: {said}"

        if not (same servedOuter (holder.querySelector "button.outer")) then
            failwith "the island replaced its button instead of adopting it"

        let copies = innerRoot.querySelectorAll(".counter").length

        if copies <> 1 || not (same servedInner (innerRoot.querySelector "button")) then
            failwith $"the component inside did not adopt the root drawn for it ({copies} copies)"

        // The hole the root was written into is empty as far as the light DOM goes: the
        // parser took the template away, and lit is rendering `nothing` there.
        if not (isNull (inner.querySelector "template")) then
            failwith "the template the root arrived in is still among the component's children"

        (servedOuter :?> HTMLElement).click ()
        (servedInner :?> HTMLElement).click ()
        do! updated inner

        if outerClicks <> 1 then
            failwith $"the island's handler ran {outerClicks} times for one click"

        if (innerRoot.querySelector ".n").textContent <> "1" then
            failwith "the click inside the component never reached its state"

        // An ordinary render of the island from here on. The component is the same
        // element, with the count it had reached, and nothing has been drawn into it.
        Lit.render holder (view 5)
        do! updated inner

        if (holder.querySelector "button.outer").textContent <> "5" then
            failwith "the island did not render its next model"

        if not (same inner (holder.querySelector "nested-counter")) then
            failwith "rendering the island replaced the component inside it"

        if (innerRoot.querySelector ".n").textContent <> "1" || innerRoot.querySelectorAll(".counter").length <> 1 then
            failwith "rendering the island disturbed what the component had drawn"

        document.body.removeChild holder |> ignore
    }

    it "is adopted in a component, and adopts its own root there" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let shadow: string = expected?("host#shadow")

        let holder = document.createElement "div"
        setHTMLUnsafe holder $"<nested-outer>{shadow}</nested-outer>"

        let outer: Element = holder?firstElementChild
        let outerRoot: ShadowRoot = outer?shadowRoot
        let inner = outerRoot.querySelector "nested-counter"
        let innerRoot: ShadowRoot = inner?shadowRoot

        if isNull (box innerRoot) then
            failwith "the browser did not attach a root drawn inside another root"

        let servedOuter = outerRoot.querySelector "button.outer"
        let servedInner = innerRoot.querySelector "button"

        document.body.appendChild holder |> ignore
        do! updated outer
        do! updated inner

        if outerRoot.querySelectorAll(".host").length <> 1 || not (same servedOuter (outerRoot.querySelector "button.outer")) then
            failwith "the outer component did not adopt the root drawn for it"

        if innerRoot.querySelectorAll(".counter").length <> 1 || not (same servedInner (innerRoot.querySelector "button")) then
            failwith "the component inside it did not adopt its own"

        (servedOuter :?> HTMLElement).click ()
        (servedInner :?> HTMLElement).click ()
        do! updated outer
        do! updated inner

        if (outerRoot.querySelector "button.outer").textContent <> "1" then
            failwith "the click in the outer component never reached its state"

        if (innerRoot.querySelector ".n").textContent <> "1" then
            failwith "the click in the inner component never reached its state"

        if not (same inner (outerRoot.querySelector "nested-counter")) then
            failwith "the outer component's update replaced the component inside it"

        document.body.removeChild holder |> ignore
    }

    // The case `defer-hydration` exists for. The component is defined before the view
    // around it is hydrated, which is the order that goes wrong: left to itself it would
    // adopt at once, with no name, and the template for no name is not the one the
    // server drew. So the server marks it, it waits, and hydrating the view both hands it
    // the name and lets it go.
    it "waits for the view around it when that view hands it a property" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let markup: string = expected?("introduces#hydratable")

        let holder = document.createElement "div"
        setHTMLUnsafe holder markup

        let inner = holder.querySelector "nested-greeting"

        if not (inner.hasAttribute "defer-hydration") then
            failwith "the server did not mark the component, so this is not the case it claims to be"

        let root: ShadowRoot = inner?shadowRoot
        let served = root.querySelector "b.somebody"

        let ear = listenToComplaints ()
        document.body.appendChild holder |> ignore
        do! Promise.sleep 50

        // In the document, defined, and still as the server left it.
        if (inner?hasUpdated: bool) then
            failwith "the component drew before the view around it had hydrated"

        if not (same served (root.querySelector "b.somebody")) || not (isNull (root.querySelector "i.nobody")) then
            failwith "the component disturbed its root while it was meant to be waiting"

        Hydrate.adopt holder (SharedViews.introduces "Ada")
        do! updated inner
        ear?stop () |> ignore

        if inner.hasAttribute "defer-hydration" then
            failwith "hydrating the view did not take the mark off the component"

        if (inner?who: string) <> "Ada" then
            failwith "hydrating the view did not hand the component its property"

        if not (same served (root.querySelector "b.somebody")) || root.querySelectorAll("b.somebody").length <> 1 then
            failwith "the component drew again instead of adopting what the server drew"

        let said: string[] = ear?said

        if said.Length <> 0 then
            failwith $"something was said along the way: {said}"

        // And it follows the view from then on, like any component handed a property.
        Lit.render holder (SharedViews.introduces "Grace")
        do! updated inner

        if (root.querySelector "b.somebody").textContent <> "Grace" then
            failwith "the component did not follow the view's next render"

        document.body.removeChild holder |> ignore
    }

    // Waiting is not having started, and leaving while waiting is not being torn down.
    // Treat it as that and the hooks are marked as torn, so that when the component does
    // start, its first render runs every `useEffectOnce` twice: once as a first run, and
    // once as the return of something that had never been there.
    it "does not count leaving while it waits as having been torn down" <| fun () -> promise {
        tenancy.Clear()

        let holder = document.createElement "div"
        document.body.appendChild holder |> ignore
        holder.innerHTML <- "<nested-waiter defer-hydration></nested-waiter>"
        let el: Element = holder?firstElementChild
        do! Promise.sleep 50

        if tenancy.Count <> 0 then
            failwith $"the component started while it was told to wait ({List.ofSeq tenancy})"

        // Moved, still waiting.
        holder.removeChild el |> ignore
        document.body.appendChild el |> ignore
        do! Promise.sleep 20

        el.removeAttribute "defer-hydration"
        do! updated el

        if List.ofSeq tenancy <> [ "in" ] then
            failwith $"the effect ran {List.ofSeq tenancy} for one start, not [in]"

        document.body.removeChild el |> ignore
        document.body.removeChild holder |> ignore
    }

    // The other meaning of the same expression. When lit renders the view itself there
    // is no server in it, the hole holds nothing, and the component draws its own root
    // as components do.
    it "draws its own root when lit renders the view, where the server's is nothing" <| fun () -> promise {
        let holder = document.createElement "div"
        document.body.appendChild holder |> ignore

        Lit.render holder (SharedViews.host { SharedViews.Count = 0 } ignore)

        let inner = holder.querySelector "nested-counter"
        do! updated inner

        if not (isNull (inner.querySelector "*")) then
            failwith "lit rendered something into the component's light DOM where there should be nothing"

        let root: ShadowRoot = inner?shadowRoot

        if isNull (box root) || root.querySelectorAll(".counter").length <> 1 then
            failwith "the component did not draw its own root"

        (root.querySelector "button" :?> HTMLElement).click ()
        do! updated inner

        if (root.querySelector ".n").textContent <> "1" then
            failwith "a component lit rendered did not respond"

        document.body.removeChild holder |> ignore
    }
