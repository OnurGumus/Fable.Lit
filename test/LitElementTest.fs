module LitElement

open Fable.Core
open Fable.Core.JsInterop
open Lit
open Expect
open Expect.Dom
open WebTestRunner
open Lit.Test

let private hmr = HMR.createToken()

[<LitElement("fable-element")>]
let MyEl () =
    Hook.useHmr(hmr)
    let _ = LitElement.init ()

    html
        $"""
        <p>Element</p>
    """

[<LitElement("fel-attribute-changes")>]
let AttributeChanges () =
    let _, props =
        LitElement.init (fun config -> config.props <- {| fName = Prop.Of("default", attribute = "f-name") |})

    html
        $"""
        <p id="value">{props.fName.Value}</p>
    """

[<LitElement("fel-attribute-doesnt-change")>]
let AttributeDoesntChange () =
    let _, props =
        LitElement.init (fun config -> config.props <- {| fName = Prop.Of("default", attribute = "") |})

    html
        $"""
        <p id="value">{props.fName.Value}</p>
    """

let reverse (str: string) =
    str.ToCharArray() |> Array.rev

[<LitElement("fel-attribute-reflects")>]
let AttributeReflects () =
    let _, props =
        LitElement.init (fun config ->
            config.props <-
                {|
                    fName = Prop.Of("default", attribute = "f-name", reflect = true)
                    revName = Prop.Of([||], attribute = "rev-name", fromAttribute=reverse)
                |})

    html
        $"""
        <p id="f-value">{props.fName.Value}</p>
        <p id="rev-value">{props.revName.Value |> Array.map string |> String.concat "-"}</p>
    """

[<LitElement("fel-dispatch-events")>]
let DispatchEvents () =
    let el = LitElement.init ()

    let onClick _ =
        el.dispatchEvent("fires-events")

    html
        $"""
        <button @click={onClick}>Click me!</button>
    """

[<LitElement("fel-dispatch-custom-events")>]
let DispatchCustomEvents () =
    let el = LitElement.init()

    let onClick _ =
        el.dispatchCustomEvent("fires-custom-events", 10)

    html
        $"""
        <button @click={onClick}>Click me!</button>
    """

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

/// innerHTML has not attached declarative shadow roots since Chrome 124, see ShadowTest.
[<Emit("$0.setHTMLUnsafe($1)")>]
let private setHTMLUnsafe (el: obj) (html: string) : unit = jsNative

[<Emit("getComputedStyle($0).color")>]
let private colourOf (el: obj) : string = jsNative

/// Everything said to console.warn while this is held.
[<Emit("(() => { const said = []; const original = console.warn; console.warn = (...args) => { said.push(args.map(String).join(' ')); original.apply(console, args); }; return { said, stop() { console.warn = original; } }; })()")>]
let private listenToWarnings () : obj = jsNative

/// A component rendering the view the server renders, on a page where nothing has
/// called `Hydrate.elements`.
[<LitElement("fel-unadopted")>]
let Unadopted () =
    let _ = LitElement.init ()
    SharedViews.counter { SharedViews.Count = 0 } ignore

/// A component, and the same component as the next version of its module defines it.
/// The second is not decorated: the test hands it to the decorator itself, which is all
/// that running the module again does.
[<LitElement("fel-redrawn")>]
let Redrawn () =
    let _ = LitElement.init ()
    html $"""<p class="v">first</p>"""

let private redrawnNext () =
    let _ = LitElement.init ()
    html $"""<p class="v">second</p>"""

describe "LitElement" <| fun () ->
    it "fable-element renders" <| fun () -> promise {
        use! el = render_html $"<fable-element></fable-element>"
        return! el.El |> Expect.matchHtmlSnapshot "fable-element"
    }

    it "Reacts to attribute/property changes" <| fun () -> promise {
        use! el = render_html $"<fel-attribute-changes></fel-attribute-changes>"
        let el = el.El
        // check the default value
        el.getSelector("#value") |> Expect.innerText "default"
        el.setAttribute("f-name", "fable")
        // wait for lit's render updates
        do! elementUpdated el
        el.getSelector("#value") |> Expect.innerText "fable"
        // update property manually
        el?fName <- "fable-2"
        do! elementUpdated el
        el.getSelector("#value") |> Expect.innerText "fable-2"
    }

    it "Doesn't react to attribute changes" <| fun () -> promise {
        use! el = render_html $"<fel-attribute-doesnt-change></fel-attribute-doesnt-change>"
        let el = el.El
        // check the default value
        el.getSelector("#value") |> Expect.innerText "default"
        el.setAttribute("f-name", "fable")
        // wait for lit's render updates
        do! elementUpdated el
        el.getSelector("#value") |> Expect.innerText "default"
        // update property manually
        el?fName <- "fable"
        do! elementUpdated el
        el.getSelector("#value") |> Expect.innerText "fable"
    }

    it "Reflect Attribute changes" <| fun () -> promise {
        use! el = render_html $"<fel-attribute-reflects></fel-attribute-reflects>"
        let el = el.El
        // check the default value
        el.getSelector("#f-value") |> Expect.innerText "default"
        el.getAttribute("f-name") |> Expect.equal "default"
        el?fName <- "fable"
        // wait for lit's render updates
        do! elementUpdated el
        // setting the property should have updated the value and the attribute
        el.getSelector("#f-value") |> Expect.innerText "fable"
        el.getAttribute("f-name") |> Expect.equal "fable"
    }

    it "From Attribute works" <| fun () -> promise {
        use! el = render_html $"""<fel-attribute-reflects rev-name="aloh"></fel-attribute-reflects>"""
        let el = el.El
        el.getSelector("#rev-value") |> Expect.innerText "h-o-l-a"
    }

    it "Fires Events" <| fun () -> promise {
        use! el = render_html $"""<fel-dispatch-events></fel-dispatch-events>"""
        let el = el.El
        do!
            Expect.dispatch
                "fires-events"
                (fun _ -> el.shadowRoot.getButton("click").click())
                el
    }

    it "Fires Custom Events" <| fun () -> promise {
        use! el = render_html $"""<fel-dispatch-custom-events></fel-dispatch-custom-events>"""
        let el = el.El
        let! result =
            Expect.dispatchCustom<int>
                "fires-custom-events"
                (fun _ -> el.shadowRoot.getButton("click").click())
                el

        Expect.equal (Some 10) result
    }

// What happens to a component the server drew when nothing on the page is set to adopt
// it. Not a feature: this is the mistake `Hydrate.elements` exists to prevent, and what
// is being tested is that it can no longer be made without being told.
describe "A component the server drew, and nobody adopts" <| fun () ->

    // Two copies: the server's, which nothing is wired to, and the component's own
    // beside it. It used to arrive without a word, and looks at a glance like a
    // rendering bug in the view.
    it "says so before it draws a second copy beside the server's" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let shadow: string = expected?("counter#shadow")

        let holder = Browser.Dom.document.createElement "div"
        setHTMLUnsafe holder $"<fel-unadopted>{shadow}</fel-unadopted>"
        let el: Browser.Types.HTMLElement = holder?firstElementChild

        let ear = listenToWarnings ()
        Browser.Dom.document.body.appendChild holder |> ignore
        do! elementUpdated el
        ear?stop () |> ignore

        let copies = el.shadowRoot.querySelectorAll(".counter").length

        if copies <> 2 then
            failwith $"expected the duplicate this warning is about, found {copies} copies"

        let said: string[] = ear?said

        if not (said |> Array.exists (fun line -> line.Contains "<fel-unadopted>" && line.Contains "Hydrate.elements")) then
            failwith $"a server-rendered root was rendered over and nothing named the element or the fix: {said}"

        Browser.Dom.document.body.removeChild holder |> ignore
    }

    // A root can be sent ahead with only a stylesheet in it, so that the component is
    // not unstyled while its script loads. There is no markup in it to duplicate, lit
    // renders in front of the style element, and none of that is anybody's mistake.
    it "says nothing about a root that only carries a stylesheet" <| fun () -> promise {
        let holder = Browser.Dom.document.createElement "div"

        setHTMLUnsafe
            holder
            """<fel-unadopted><template shadowrootmode="open"><style>.n { color: rgb(1, 2, 3); }</style></template></fel-unadopted>"""

        let el: Browser.Types.HTMLElement = holder?firstElementChild

        let ear = listenToWarnings ()
        Browser.Dom.document.body.appendChild holder |> ignore
        do! elementUpdated el
        ear?stop () |> ignore

        let said: string[] = ear?said

        if said |> Array.exists (fun line -> line.Contains "Hydrate.elements") then
            failwith $"warned about a shadow root with no markup in it: {said}"

        let copies = el.shadowRoot.querySelectorAll(".counter").length

        if copies <> 1 then
            failwith $"expected the component's one render, found {copies}"

        if colourOf (el.shadowRoot.querySelector ".n") <> "rgb(1, 2, 3)" then
            failwith "the stylesheet that was sent ahead no longer applies"

        Browser.Dom.document.body.removeChild holder |> ignore
    }

// A hot update that reaches a component's module through one that accepts its own
// updates runs the module again, and with it the decorator. The element cannot be
// defined twice, so without this the page would keep drawing it with the first version,
// against data an Elmish program may already have rebuilt for the second.
describe "A component whose module runs again" <| fun () ->
    it "redraws what is on the page, and draws what comes later, with the new code" <| fun () -> promise {
        let holder = Browser.Dom.document.createElement "div"
        Browser.Dom.document.body.appendChild holder |> ignore
        holder.innerHTML <- "<fel-redrawn></fel-redrawn>"
        let before: Browser.Types.HTMLElement = holder?firstElementChild
        do! elementUpdated before

        let shown (el: Browser.Types.HTMLElement) = el.shadowRoot.querySelector(".v").textContent

        if shown before <> "first" then
            failwith $"expected the first version to draw first, got {shown before}"

        LitElementAttribute("fel-redrawn").Decorate(unbox redrawnNext, unbox (createObj [ "name" ==> "Redrawn" ]))
        |> ignore

        do! Promise.sleep 50

        if shown before <> "second" then
            failwith $"the element already on the page still draws with the first version ({shown before})"

        holder.insertAdjacentHTML ("beforeend", "<fel-redrawn></fel-redrawn>")
        let later: Browser.Types.HTMLElement = holder?lastElementChild
        do! elementUpdated later

        if shown later <> "second" then
            failwith $"an element added afterwards draws with the first version ({shown later})"

        Browser.Dom.document.body.removeChild holder |> ignore
    }
