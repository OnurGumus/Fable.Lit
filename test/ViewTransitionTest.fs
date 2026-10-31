/// Running a change inside a document view transition.
///
/// The browser takes a picture before the change and another after it, and animates
/// between the two. What these tests hold it to is what a page sees: the after-picture
/// shows what the change drew, transitions asked for in quick succession all run rather
/// than cutting each other short, and a failure in one does not stop the rest.
module ViewTransitionTest

open Browser
open Fable.Core
open Fable.Core.JsInterop
open Lit
open WebTestRunner

/// A component that shows the text it is handed.
[<LitElement("vt-label")>]
let Label () =
    let _, props =
        LitElement.init (fun config -> config.props <- {| text = Prop.Of("before", attribute = "") |})

    html $"<b>{props.text.Value}</b>"

/// Names its own element from the text it is handed, during render, so the browser's
/// after-picture says which text it saw.
[<LitElement("vt-inner")>]
let Inner () =
    let host, props =
        LitElement.init (fun config -> config.props <- {| text = Prop.Of("before", attribute = "") |})

    host?style?viewTransitionName <- "vt-" + props.text.Value
    html $"<i>{props.text.Value}</i>"

/// Hands its text on to the inner component, as a board hands a card its card.
///
/// Without a shadow root of its own, so the inner component is in the page, where the
/// browser looks for names when it takes its pictures.
[<LitElement("vt-outer")>]
let Outer () =
    let _, props =
        LitElement.init (fun config ->
            config.useShadowDom <- false
            config.props <- {| text = Prop.Of("before", attribute = "") |})

    html $"<vt-inner .text={props.text.Value}></vt-inner>"

/// Watches the transitions the page starts: the names of the new pictures in each, and
/// whether each one ran or was cut short by the next.
[<Emit("""(() => {
    const original = document.startViewTransition;
    const result = { pictures: [], outcomes: [], restore() { document.startViewTransition = original; } };
    document.startViewTransition = function (update) {
        const transition = original.call(document, update);
        const index = result.outcomes.push('pending') - 1;
        transition.ready.then(
            () => {
                result.outcomes[index] = 'ran';
                result.pictures.push(...document.getAnimations()
                    .map(a => a.effect && a.effect.pseudoElement)
                    .filter(p => p && p.startsWith('::view-transition-new')));
            },
            error => { result.outcomes[index] = error.name; });
        return transition;
    };
    return result;
})()""")>]
let private watchTransitions () : obj = jsNative

/// Takes the browser's view transitions away, as an older browser has none.
[<Emit("(() => { const original = document.startViewTransition; document.startViewTransition = undefined; return () => { document.startViewTransition = original; }; })()")>]
let private withoutViewTransitions () : (unit -> unit) = jsNative

[<Emit("document.documentElement.dataset.transition")>]
let private pageMark () : string = jsNative

/// Everything said to console.error while this is held.
[<Emit("(() => { const said = []; const original = console.error; console.error = (...args) => { said.push(args.map(a => a?.message ?? String(a)).join(' ')); }; return { said, stop() { console.error = original; } }; })()")>]
let private listenToErrors () : obj = jsNative

let private shown (el: Browser.Types.HTMLElement) : string = el?shadowRoot?textContent

describe "ViewTransition" <| fun () ->

    // The browser takes the after-picture once the change's own microtasks have run, and
    // a component's render is one of them, as is the render it hands a nested component.
    // So the change needs no waiting of its own, and this holds that true.
    it "takes the after-picture once a nested component has rendered" <| fun () -> promise {
        let outer = document.createElement "vt-outer"
        document.body.appendChild outer |> ignore
        do! (outer :?> LitElement).updateComplete
        do! Promise.sleep 0
        let watch = watchTransitions ()

        try
            do! ViewTransition.run "test" (fun () -> outer?text <- "after")
        finally
            watch?restore ()

        document.body.removeChild outer |> ignore
        let pictures: string[] = watch?pictures

        if not (pictures |> Array.contains "::view-transition-new(vt-after)") then
            failwith $"the after-picture was taken before the inner component rendered: {pictures}"
    }

    // A document runs one transition at a time, and starting another cuts the running one
    // short. Queued, each runs to the end, in the order asked for.
    it "runs transitions asked for together one after another, none cut short" <| fun () -> promise {
        let watch = watchTransitions ()
        let order = ResizeArray<string>()

        try
            ViewTransition.run "test" (fun () -> order.Add "first") |> ignore
            ViewTransition.run "test" (fun () -> order.Add "second") |> ignore
            do! ViewTransition.run "test" (fun () -> order.Add "third")
        finally
            watch?restore ()

        let outcomes: string[] = watch?outcomes

        if outcomes <> [| "ran"; "ran"; "ran" |] then
            failwith $"the transitions cut each other short: {outcomes}"

        if List.ofSeq order <> [ "first"; "second"; "third" ] then
            failwith $"the changes ran out of order: {List.ofSeq order}"
    }

    it "keeps going after a change that throws, and says so" <| fun () -> promise {
        let errors = listenToErrors ()
        let mutable after = false

        try
            ViewTransition.run "test" (fun () -> failwith "broken") |> ignore
            do! ViewTransition.run "test" (fun () -> after <- true)
        finally
            errors?stop ()

        let said: string[] = errors?said

        if not after then
            failwith "the change after a failed one never ran"

        if not (said |> Array.exists (fun line -> line.Contains "broken")) then
            failwith $"the failure was not reported: {said}"
    }

    it "marks the page with the kind only while the transition runs" <| fun () -> promise {
        let mutable during = ""
        do! ViewTransition.run "kanban" (fun () -> during <- pageMark ())

        if during <> "kanban" then
            failwith $"the change ran with the page marked '{during}'"

        if not (isNull (box (pageMark ()))) then
            failwith $"the page is still marked '{pageMark ()}' after the transition"
    }

    it "makes the change without animating where there are no view transitions" <| fun () -> promise {
        let el = document.createElement "vt-label"
        document.body.appendChild el |> ignore
        do! (el :?> LitElement).updateComplete
        let restore = withoutViewTransitions ()
        let mutable during = "unset"

        try
            do! ViewTransition.run "test" (fun () ->
                during <- pageMark ()
                el?text <- "after")
        finally
            restore ()

        do! (el :?> LitElement).updateComplete
        let now = shown el
        document.body.removeChild el |> ignore

        if now <> "after" then
            failwith $"the change was not drawn (shows {now})"

        if not (isNull (box during)) then
            failwith $"the page was marked '{during}' with nothing to animate"
    }

    it "dispatches a message inside a transition" <| fun () -> promise {
        let received = ResizeArray<int>()
        ViewTransition.dispatch "test" received.Add 7
        do! ViewTransition.run "test" ignore

        if List.ofSeq received <> [ 7 ] then
            failwith $"the message did not arrive: {List.ofSeq received}"
    }
