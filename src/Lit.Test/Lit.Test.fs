module Lit.Test

open System
open Fable.Core
open Fable.Core.JsInterop
open Browser
open Browser.Types
open Expect.Dom
open Lit

/// For LitElements, awaits `el.updateComplete`.
/// For the rest, awaits the render queue: two macrotasks, which is what this library
/// schedules its own renders on. Neither path waits for paint.
///
/// It used to be one animation frame, and that is a trap rather than a preference.
/// Chrome suspends `requestAnimationFrame` outright in a tab that is not visible, and
/// a test runner with any concurrency at all puts every file but one in a background
/// tab -- so a suite awaiting this hung until the runner's timeout, in exactly the
/// tests written against plain elements, while every LitElement test beside them
/// passed on `updateComplete`. Ten of thirty-one here, and the flags that look like
/// they would fix it (`--disable-renderer-backgrounding` and friends) are already
/// passed by puppeteer and govern timers and renderer priority, not frames.
///
/// A frame was never the right thing to wait for. `HookUtil.runAsync` renders on
/// `delay 0` -- see the note there about Firefox skipping renders under rAF -- so the
/// DOM is settled once that macrotask has run, and nothing here needs the browser to
/// have painted: assertions read `innerText`, serialise the DOM, or call
/// `getComputedStyle`, which forces style and layout synchronously by itself.
///
/// Two ticks rather than one: an update that schedules another update needs the
/// second, and at four milliseconds it is the cheapest insurance in this file.
let elementUpdated (el: Element) =
    match box el with
    | :? LitElement as el -> el.updateComplete
    | _ -> Promise.sleep 0 |> Promise.bind (fun () -> Promise.sleep 0)
    // Check if ShadyDOM polyfill is being used
    // https://github.com/webcomponents/polyfills/tree/master/packages/shadydom
    |> Promise.map (fun () ->
        emitJsStatement () """
            if (window.ShadyDOM && typeof window.ShadyDOM.flush === 'function') {
                window.ShadyDOM.flush();
            }"""
    )

/// Clicks a button and awaits for the element to be updated
let click (el: Element) (button: HTMLButtonElement) =
    button.click()
    elementUpdated el

/// Creates a div container, puts it in `document.body`, renders the template onto it,
/// waits until render is complete and returns first element child.
/// When disposed, the container will be removed from `document.body`.
let render (template: TemplateResult): JS.Promise<Container> = promise {
    let container = Container.New()
    Lit.render container.El template
    // TODO: We should have firstElementChild in Browser.Dom
    let el: HTMLElement = container.El?firstElementChild
    do! elementUpdated el
    return { new Container with
                member _.El = el
                member _.Dispose() = container.Dispose() }
}

/// Creates a div container, puts it in `document.body`, renders the template onto it,
/// waits until render is complete and returns first element child.
/// When disposed, the container will be removed from `document.body`.
let render_html (template: FormattableString) =
    html template |> render

[<RequireQualifiedAccess>]
module Program =
    /// Mounts an element to the DOM to render the Elmish app and returns the container
    /// with an extra property to retrieve the model.
    let mountAndTestWith (arg: 'arg) (program: Elmish.Program<'arg, 'model, 'msg, Lit.TemplateResult>) =
        Expect.Elmish.Program.mountAndTestWith Lit.render arg program

    /// Mounts an element to the DOM to render the Elmish app and returns the container
    /// with an extra property to retrieve the model.
    let mountAndTest (program: Elmish.Program<unit, 'model, 'msg, Lit.TemplateResult>) =
        Expect.Elmish.Program.mountAndTest Lit.render program
