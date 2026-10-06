/// Effects, and a component that moves or leaves before they have run.
///
/// A hook's effects do not run when the component renders. They run a moment later, on
/// a timer, so that the DOM they look at is the one just rendered. Between the render
/// and that moment the component can leave the page, or leave and come back, and each
/// of those used to end somewhere it should not: an effect set up for a component that
/// had already gone, which nothing would ever tear down, or an effect set up twice.
///
/// A component reaches these by being quick, not by being unusual: created and moved
/// in one go, or rendered and rendered away again before the timer fires.
module EffectTimingTest

open Browser
open Browser.Types
open Lit
open WebTestRunner

/// What the components below set up and tore down, in order.
let private tenancy = ResizeArray<string>()

// The effect is written out in each, not shared: a hook finds its component through
// the function it is called from, so it has to be called from the component itself.

[<LitElement("effect-timing-element")>]
let TimedElement () =
    LitElement.init () |> ignore

    Hook.useEffectOnce (fun () ->
        tenancy.Add "in"
        Hook.createDisposable (fun () -> tenancy.Add "out"))

    html $"<i>element</i>"

[<HookComponent>]
let TimedComponent () =
    Hook.useEffectOnce (fun () ->
        tenancy.Add "in"
        Hook.createDisposable (fun () -> tenancy.Add "out"))

    html $"<i>component</i>"

/// An element with a hook component inside it.
[<LitElement("effect-timing-landlord")>]
let TimedLandlord () =
    LitElement.init () |> ignore
    html $"<p>{TimedComponent()}</p>"

let private holder () =
    let el = document.createElement "div"
    document.body.appendChild el |> ignore
    el

describe "Effects, and leaving before they run" <| fun () ->

    // Connected, disconnected and connected again in one task, before its first render.
    // Wrapping an element that has just been inserted does this, and so does anything
    // that re-parents what it was handed.
    it "sets up once for an element that is moved before it has rendered" <| fun () -> promise {
        tenancy.Clear()
        let first = holder ()
        let second = holder ()

        let el = document.createElement "effect-timing-element"
        first.appendChild el |> ignore
        second.appendChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in" ] then
            failwith $"an element that was moved before its first render set up {List.ofSeq tenancy}, not [in]"

        second.removeChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in"; "out" ] then
            failwith $"leaving tore down {List.ofSeq tenancy}, not what was set up"

        document.body.removeChild first |> ignore
        document.body.removeChild second |> ignore
    }

    // Rendered, and gone before the timer that runs its effects. Whatever they set up
    // would belong to an element that has already had its one chance to tear it down.
    it "sets nothing up for an element that has left by the time its effects would run" <| fun () -> promise {
        tenancy.Clear()
        let home = holder ()

        let el = document.createElement "effect-timing-element"
        home.appendChild el |> ignore
        // Resolves as soon as the element has rendered, which is ahead of any timer.
        do! (el :?> LitElement).updateComplete
        home.removeChild el |> ignore
        do! Promise.sleep 50

        if tenancy.Count <> 0 then
            failwith $"an effect was set up for an element that had already left ({List.ofSeq tenancy}), and nothing is left to tear it down"

        // Coming back is an arrival like any other: set up, once.
        home.appendChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in" ] then
            failwith $"coming back set up {List.ofSeq tenancy}, not [in]"

        home.removeChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in"; "out" ] then
            failwith $"leaving again tore down {List.ofSeq tenancy}"

        document.body.removeChild home |> ignore
    }

    // The same thing for the directive form, which learns that it has gone by a
    // different route: lit clears the part it was rendered into.
    it "sets nothing up for a hook component that is rendered away before its effects would run" <| fun () -> promise {
        tenancy.Clear()
        let home = holder ()

        Lit.render home (html $"<div>{TimedComponent()}</div>")
        Lit.render home (html $"<div>{Lit.nothing}</div>")
        do! Promise.sleep 50

        if tenancy.Count <> 0 then
            failwith $"an effect was set up for a hook component that had already been rendered away ({List.ofSeq tenancy})"

        document.body.removeChild home |> ignore
    }

    // An element that is put in and taken straight out again still renders, a moment
    // later, while it is out. A hook component inside it is therefore created in a tree
    // that is not connected, and correctly sets nothing up. What it must not do is take
    // that for the whole story: when the element does arrive, so has it.
    it "sets up for a hook component whose element first rendered while it was out of the page" <| fun () -> promise {
        tenancy.Clear()
        let home = holder ()

        let el = document.createElement "effect-timing-landlord"
        home.appendChild el |> ignore
        home.removeChild el |> ignore
        do! (el :?> LitElement).updateComplete
        do! Promise.sleep 50

        if tenancy.Count <> 0 then
            failwith $"something was set up inside an element that is not in the page ({List.ofSeq tenancy})"

        home.appendChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in" ] then
            failwith $"the element arrived and the hook component inside it set up {List.ofSeq tenancy}, not [in]"

        home.removeChild el |> ignore
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in"; "out" ] then
            failwith $"the element left and the hook component inside it tore down {List.ofSeq tenancy}"

        document.body.removeChild home |> ignore
    }

    // And the ordinary case, so that the ones above cannot be passed by never running
    // an effect at all.
    it "still sets up and tears down for a component that stays long enough" <| fun () -> promise {
        tenancy.Clear()
        let home = holder ()

        Lit.render home (html $"<div>{TimedComponent()}</div>")
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in" ] then
            failwith $"a hook component that stayed set up {List.ofSeq tenancy}, not [in]"

        Lit.render home (html $"<div>{Lit.nothing}</div>")
        do! Promise.sleep 50

        if List.ofSeq tenancy <> [ "in"; "out" ] then
            failwith $"rendering it away tore down {List.ofSeq tenancy}"

        document.body.removeChild home |> ignore
    }
