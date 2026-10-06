/// A component that arrives with its shadow root already drawn, and takes it over.
///
/// The island tests adopt markup in a container somebody else owns: a div, or a shadow
/// root on a plain host. These are about the other kind of thing on a page -- a
/// `[<LitElement>]`, which the browser constructs, which renders itself, and which until
/// `Hydrate.elements` had no way to notice that the server had got there first. It
/// rendered into the root as though it were empty, and the page showed the server's copy
/// with a working one underneath it.
///
/// As elsewhere, the markup is what the .NET renderer actually writes, and adoption is
/// asserted the only way it can be: the node the server sent is still the node.
module ComponentHydrateTest

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

[<Emit("getComputedStyle($0)[$1]")>]
let private styleOf (el: obj) (property: string) : string = jsNative

/// Everything said to console.warn or console.error while this is held.
[<Emit("(() => { const said = []; const originals = { warn: console.warn, error: console.error }; for (const level of ['warn', 'error']) { console[level] = (...args) => { said.push(args.map(String).join(' ')); originals[level].apply(console, args); }; } return { said, stop() { Object.assign(console, originals); } }; })()")>]
let private listenToComplaints () : obj = jsNative

/// lit's own digest, for the one test whose markup the .NET renderer cannot write.
[<Import("digestForTemplateResult", "@lit-labs/ssr-client")>]
let private digestOf (template: TemplateResult) : string = jsNative

// Once for the page, the way an application says it at startup. It is a call and not an
// import, so where it comes among the imports above is of no interest to anybody.
Hydrate.elements ()

/// How many times the component below has been asked to render.
let mutable private renders = 0

/// The view the server rendered, driven by a hook where the island tests use a program.
///
/// With a stylesheet of its own, which says something different from the one the server
/// sends: both have to be in force once the component has taken the root over.
[<LitElement("adopt-counter")>]
let AdoptCounter () =
    LitElement.init (fun config -> config.styles <- [ Lit.unsafeCSS ".n { font-weight: 700; }" ])
    |> ignore

    let count, setCount = Hook.useState 0

    // Counted after the first hook, which the dry run that reads the configuration when
    // the element is defined never gets past.
    renders <- renders + 1

    SharedViews.counter { SharedViews.Count = count } (fun _ -> setCount (count + 1))

/// What a hook component set up and tore down, in order.
let private tenancy = ResizeArray<string>()

let private tenantView () = html $"<i>tenant</i>"

/// A hook component with something to clean up, to live inside an element.
[<HookComponent>]
let Tenant () =
    Hook.useEffectOnce (fun () ->
        tenancy.Add "in"
        Hook.createDisposable (fun () -> tenancy.Add "out"))

    tenantView ()

let private landlordView () = html $"<p>{Tenant()}</p>"

[<LitElement("adopt-landlord")>]
let Landlord () =
    LitElement.init () |> ignore
    landlordView ()

// Components for the warning about stylesheets, a tag apiece because it is said once
// for a tag. Each draws the same view; what differs is the stylesheet it calls its own,
// held against the one the server sends, which is `.n { color: rgb(1, 2, 3); }`.

/// Has a stylesheet, and is sent none.
[<LitElement("sheet-unsent")>]
let SheetUnsent () =
    LitElement.init (fun config -> config.styles <- [ Lit.unsafeCSS ".n { font-weight: 700; }" ])
    |> ignore

    SharedViews.counter { SharedViews.Count = 0 } ignore

/// Has a stylesheet, and is sent a different one.
[<LitElement("sheet-other")>]
let SheetOther () =
    LitElement.init (fun config -> config.styles <- [ Lit.unsafeCSS ".n { font-weight: 700; }" ])
    |> ignore

    SharedViews.counter { SharedViews.Count = 0 } ignore

/// Has the stylesheet it is sent, written out differently.
[<LitElement("sheet-same")>]
let SheetSame () =
    LitElement.init (fun config ->
        config.styles <- [ Lit.unsafeCSS "\n    .n   {\n        color: rgb(1, 2, 3);\n    }\n" ])
    |> ignore

    SharedViews.counter { SharedViews.Count = 0 } ignore

/// Has no stylesheet of its own, and is sent one.
[<LitElement("sheet-none")>]
let SheetNone () =
    LitElement.init () |> ignore
    SharedViews.counter { SharedViews.Count = 0 } ignore

/// Has a stylesheet and is sent none, twice over.
[<LitElement("sheet-twice")>]
let SheetTwice () =
    LitElement.init (fun config -> config.styles <- [ Lit.unsafeCSS ".n { font-weight: 700; }" ])
    |> ignore

    SharedViews.counter { SharedViews.Count = 0 } ignore

/// A component as it arrives from a server: parsed, with the parser having attached
/// whatever shadow root the markup carried, and not yet in the document -- so not yet
/// upgraded, and what is in the root is the server's and nobody else's.
let private arrive (markup: string) =
    let holder = document.createElement "div"
    setHTMLUnsafe holder markup
    let el: HTMLElement = holder?firstElementChild
    let root: ShadowRoot = el?shadowRoot
    holder, el, root

/// Into the document, which is when the browser upgrades it, and through its first
/// update. The extra wait is for effects, which Fable.Lit runs a macrotask after render.
let private connect (holder: HTMLElement) (el: HTMLElement) = promise {
    document.body.appendChild holder |> ignore
    do! (el :?> LitElement).updateComplete
    do! Promise.sleep 50
}

let private settle (el: HTMLElement) = promise {
    do! (el :?> LitElement).updateComplete
    do! Promise.sleep 50
}

describe "A component the server drew" <| fun () ->

    it "takes over the shadow root instead of drawing beside it" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let shadow: string = expected?("counter#shadow")
        let holder, el, root = arrive $"<adopt-counter>{shadow}</adopt-counter>"

        if isNull (box root) then
            failwith "the browser did not attach the declarative shadow root"

        let served = root.querySelector "button"
        renders <- 0
        do! connect holder el

        let copies = root.querySelectorAll(".counter").length

        if copies <> 1 then
            failwith $"the root holds {copies} copies of the view: the component rendered beside the server's markup"

        if not (obj.ReferenceEquals(served, root.querySelector "button")) then
            failwith "the component replaced the server's button instead of taking it over"

        // Adopting needs the template before lit-element asks for it, and lit-element
        // then asks. Answering by running the function again would run every
        // `Hook.useEffect` of the first render twice.
        if renders <> 1 then
            failwith $"the component's function ran {renders} times for its first render"

        (served :?> HTMLElement).click ()
        do! settle el

        let shown = (root.querySelector ".n").textContent

        if shown <> "1" then
            failwith $"the click never reached the component's state (shows {shown})"

        if not (obj.ReferenceEquals(served, root.querySelector "button")) then
            failwith "the update rebuilt the root instead of patching the adopted nodes"

        document.body.removeChild holder |> ignore
    }

    // The server's `<style>` is in the root ahead of the markers, and is what styles the
    // component before any script has run. The component's own stylesheet is adopted on
    // top of it, as it is for a component nobody rendered in advance -- so a hot update
    // has something to replace, and a server that sent no styles costs nothing.
    it "keeps the stylesheet the server sent, and adopts its own as well" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let shadow: string = expected?("counter#shadow")
        let holder, el, root = arrive $"<adopt-counter>{shadow}</adopt-counter>"

        do! connect holder el

        let n = root.querySelector ".n"

        if styleOf n "color" <> "rgb(1, 2, 3)" then
            failwith $"""the server's stylesheet did not survive adoption ({styleOf n "color"})"""

        if styleOf n "fontWeight" <> "700" then
            failwith $"""the component's own stylesheet was never adopted ({styleOf n "fontWeight"})"""

        document.body.removeChild holder |> ignore
    }

    // The one that decides whether adopting is safe to switch on at all.
    //
    // lit-element tells a rendered tree that its element has left the page through the
    // root part its own render handed it. A component that adopts never rendered, so
    // unless something hands lit-element that part the tree is never told, and a hook
    // component inside it keeps its timers and subscriptions for as long as the tab is
    // open. lit's own hydrate support has exactly this hole; see LitHydrateSupportTest.
    //
    // The markup is written here rather than by the server, because a hook component is
    // not something a shared view can contain. The digests are lit's own.
    it "still tears down a hook component inside it when it leaves the page" <| fun () -> promise {
        tenancy.Clear()

        let markup =
            $"""<adopt-landlord><template shadowrootmode="open"><!--lit-part {digestOf (landlordView ())}--><p><!--lit-part {digestOf (tenantView ())}--><i>tenant</i><!--/lit-part--></p><!--/lit-part--></template></adopt-landlord>"""

        let holder, el, root = arrive markup
        let served = root.querySelector "i"
        do! connect holder el

        if not (obj.ReferenceEquals(served, root.querySelector "i")) || root.querySelectorAll("i").length <> 1 then
            failwith "the component did not adopt, so this proves nothing about a tree that was adopted"

        if List.ofSeq tenancy <> [ "in" ] then
            failwith $"the effect ran {List.ofSeq tenancy} on arrival, not [in]"

        holder.removeChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in"; "out" ] then
            failwith $"the element left the page and the hook component inside it was never told ({List.ofSeq tenancy})"

        // And back, because an element that was only moved should keep what it had.
        holder.appendChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in"; "out"; "in" ] then
            failwith $"the element came back and the hook component stayed torn down ({List.ofSeq tenancy})"

        document.body.removeChild holder |> ignore
    }

    // The failure path. Markup for a different view cannot be adopted, and what must not
    // happen is the thing adoption exists to prevent: two copies. It takes the server's
    // stylesheet with it when it clears the root, which is the other reason the
    // component's own is adopted first.
    it "draws once, not twice, when what arrived cannot be adopted" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let shadow: string = expected?("clickable#shadow")
        let holder, el, root = arrive $"<adopt-counter>{shadow}</adopt-counter>"

        do! connect holder el

        let stale = root.querySelectorAll("button.act").length

        if stale <> 0 then
            failwith $"the markup that was refused is still in the root ({stale} button(s) beside the new render)"

        let copies = root.querySelectorAll(".counter").length

        if copies <> 1 then
            failwith $"expected exactly one rendered view after falling back, found {copies}"

        if styleOf (root.querySelector ".n") "fontWeight" <> "700" then
            failwith "falling back left the component without its own stylesheet"

        (root.querySelector "button" :?> HTMLElement).click ()
        do! settle el

        if (root.querySelector ".n").textContent <> "1" then
            failwith "what was rendered after falling back is not wired up"

        document.body.removeChild holder |> ignore
    }

    // Switching adoption on is a statement about components the server drew. One it did
    // not draw has no root to find, and has to go on exactly as before.
    it "is left alone when the server sent the tag and nothing in it" <| fun () -> promise {
        let holder, el, before = arrive "<adopt-counter></adopt-counter>"

        if not (isNull (box before)) then
            failwith "an empty tag arrived with a shadow root, so this is not the case it claims to be"

        do! connect holder el

        let root: ShadowRoot = el?shadowRoot
        let copies = root.querySelectorAll(".counter").length

        if copies <> 1 then
            failwith $"a component nobody rendered in advance drew {copies} copies of itself"

        (root.querySelector "button" :?> HTMLElement).click ()
        do! settle el

        if (root.querySelector ".n").textContent <> "1" then
            failwith "a component nobody rendered in advance stopped responding"

        if styleOf (root.querySelector ".n") "fontWeight" <> "700" then
            failwith "a component nobody rendered in advance lost its stylesheet"

        document.body.removeChild holder |> ignore
    }

    // A root can be sent ahead with nothing but a stylesheet in it, so that the component
    // is not unstyled while its script loads. There is nothing in it to adopt, and it is
    // not a failed adoption either: lit-element renders in front of the style element,
    // as it does on a page that never heard of any of this, and nobody is told anything.
    it "leaves a root that only carries a stylesheet to lit-element, without complaint" <| fun () -> promise {
        let holder, el, root =
            arrive
                """<adopt-counter><template shadowrootmode="open"><style>.n { color: rgb(1, 2, 3); }</style></template></adopt-counter>"""

        let ear = listenToComplaints ()
        do! connect holder el
        ear?stop () |> ignore

        let said: string[] = ear?said

        if said.Length <> 0 then
            failwith $"a root with no markup in it was treated as something to adopt: {said}"

        let copies = root.querySelectorAll(".counter").length

        if copies <> 1 then
            failwith $"expected the component's one render, found {copies}"

        if styleOf (root.querySelector ".n") "color" <> "rgb(1, 2, 3)" then
            failwith "the stylesheet that was sent ahead no longer applies"

        document.body.removeChild holder |> ignore
    }

// A component's stylesheet is in two places once the page is running: the one the server
// wrote into the root, and the one the component calls its own and adopts when it
// starts. Nothing makes them the same but whoever wrote the page, and nothing breaks
// when they are not: the component arrives looking one way and changes when its script
// has loaded. On a developer's machine that is a few milliseconds and nobody sees it.
// So a development build says so, which is the build these tests are.
describe "A component the server drew, and its stylesheet" <| fun () ->

    /// A component arriving with a root, and what was said while it took it over.
    let arriving (tags: string list) (root: string) = promise {
        let holder = document.createElement "div"
        setHTMLUnsafe holder (tags |> List.map (fun tag -> $"<{tag}>{root}</{tag}>") |> String.concat "")

        let ear = listenToComplaints ()
        document.body.appendChild holder |> ignore

        for i in 0 .. int holder.children.length - 1 do
            do! (holder.children.[i] :?> LitElement).updateComplete

        do! Promise.sleep 50
        ear?stop () |> ignore
        document.body.removeChild holder |> ignore

        let said: string[] = ear?said
        return said
    }

    /// A root as the server writes it for the counter, with no stylesheet in it.
    let unstyled (expected: obj) =
        let content: string = expected?("counter#hydratable")
        $"<template shadowrootmode=\"open\">{content}</template>"

    it "says so when the server sent no stylesheet and the component has one" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let! said = arriving [ "sheet-unsent" ] (unstyled expected)

        if not (said |> Array.exists (fun line -> line.Contains "<sheet-unsent>" && line.Contains "without a stylesheet")) then
            failwith $"a component with a stylesheet arrived unstyled and nothing said so: {said}"
    }

    it "says so when the stylesheet the server sent is not the component's" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let styled: string = expected?("counter#shadow")
        let! said = arriving [ "sheet-other" ] styled

        if not (said |> Array.exists (fun line -> line.Contains "<sheet-other>" && line.Contains "different stylesheet")) then
            failwith $"a component arrived under somebody else's stylesheet and nothing said so: {said}"
    }

    // The same rules are the same stylesheet, whatever the indentation. One side is
    // usually a literal in a source file and the other the same text after it has been
    // through something, and a warning that went off on white space would be switched
    // off the first week.
    it "says nothing when the two are the same, however each is laid out" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let styled: string = expected?("counter#shadow")
        let! said = arriving [ "sheet-same" ] styled

        if said.Length <> 0 then
            failwith $"the same stylesheet on both sides was complained about: {said}"
    }

    // Nothing of its own to differ from. What the server sent stays, and is all there is.
    it "says nothing for a component that has no stylesheet of its own" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let styled: string = expected?("counter#shadow")
        let! said = arriving [ "sheet-none" ] styled

        if said.Length <> 0 then
            failwith $"a component with no stylesheet of its own was complained about: {said}"
    }

    // It is a statement about how the page was written, not about any one element, and
    // a list of two hundred of them should not say it two hundred times.
    it "says it once for a tag, however many of them arrive" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let! said = arriving [ "sheet-twice"; "sheet-twice"; "sheet-twice" ] (unstyled expected)

        let about = said |> Array.filter (fun line -> line.Contains "<sheet-twice>")

        if about.Length <> 1 then
            failwith $"three of one tag arrived and it was said {about.Length} times: {said}"
    }
