# Fable.Lit

> ### This is an unofficial fork
>
> The original lives at [fable-compiler/Fable.Lit](https://github.com/fable-compiler/Fable.Lit) and is the one to use if it does what you need. Its last release was in 2022, so this fork exists to publish fixes that are waiting on upstream. The packages it publishes are the same libraries under `.Unofficial` ids — `Fable.Lit.Unofficial`, `Fable.Lit.Elmish.Unofficial`, and so on — and **the namespaces and modules are unchanged**, so switching is one line in your package references and nothing in your F# code.
>
> What differs from upstream:
>
> - **Toolchain**: .NET 10, Fable 5, Fable.Core 4, Elmish 5, lit 3. Upstream targets .NET 6, Fable 3, Fable.Core 3, Elmish 3, lit 2.
> - **`Ev` no longer raises FS3884.** An event binding put a function value in an interpolated string, so every `@click={Ev(fun _ -> ...)}` anyone wrote reported a warning that was always wrong. `Ev` now returns an erased type, which the compiler is happy with and which compiles to exactly the same JavaScript — verified by diffing the emitted output before and after.
>
> - **`Lit.Test.elementUpdated` waits for the render queue rather than an animation frame.** Chrome suspends `requestAnimationFrame` in a tab that is not visible, and a test runner with any concurrency puts every file but one in a background tab — so suites hung on the plain-element path until their timeout while every LitElement test beside them passed. This one is worth having even if you need nothing else here.
> - **New: `Hook.useEventListener`.** Subscribes for the lifetime of the component and unsubscribes on disconnect, reading the handler through a ref so it always runs with the latest render's values without re-subscribing every frame.
> - **New: `Lit.Server.Unofficial` — the same templates rendered on .NET.** A view written once as `html $"..."` compiles by Fable into a lit template in the browser, and by the .NET compiler into HTML on the server. No Node, no JavaScript runtime, no `@lit-labs/ssr`. Text and attribute holes are escaped, `?bool` becomes a bare attribute or nothing, `@click` and `.prop` leave with their values, nested templates and lists render in place, and anything it cannot honour faithfully — `styleMap`, `until`, `repeat` — raises rather than guessing. It builds on [HtmlTypeProvider](https://github.com/OnurGumus/HtmlTypeProvider): a rendered template is a `Node`, which is exactly what a page template's hole accepts, so an island's markup composes into a server-rendered page without either side touching strings. It does **not** hydrate; the client renders over what the server sent, which is what islands already do with hand-written skeletons — the point is that the skeleton no longer has to be written twice.
> - **New: Elmish hydration.** `Program.withLitHydrated` adopts server-rendered markup on an Elmish program's first render, and renders normally from then on.
> - **New: Elmish programs survive a hot update.** Mounting a program on an element that already has one stops the first — its subscriptions and its dispatch loop, through Elmish's own termination — and in a development build starts the second from the model the first had reached. From the page's point of view that is all a hot update is: the module runs again, and mounts again.
>
>   `Fable.Elmish.HMR` already does the save-restore-stop part, which is why upstream dropped its own in 1.4.0, and it is not React-only whatever its React-shaped surface suggests. Two things differ here. It reads and writes the bundler's hot data, so with no Vite, webpack or Parcel present it does nothing at all; this is recorded on the element, so a page that re-imports its own rebuilt bundle behaves identically — no bundler HMR API in sight. And it knows about hydration: the second mount must *render*, because lit refuses to hydrate a container it is already rendering into, and the fallback from that refusal is the page rebuilt from scratch. Nothing changes your message type either. `HMR.acceptSelf()` is the one line that opts a Vite or webpack entry module in, for those who want the bundler to drive it.
> - **New: declarative shadow DOM.** `Lit.Server`'s `toShadowRootNode` writes a view into a `<template shadowrootmode="open">`, styles and all, so an element arrives with its shadow DOM already built by the HTML parser rather than waiting for script to build one; `Program.withLitHydratedInShadowRoot` adopts it there. Nothing in the marker protocol needed changing — lit finds its bindings by walking comment nodes, and comments live in a shadow root like anywhere else — so this is a helper and a mount point, not a second renderer. It is the islands version: rendering `LitElement` components server-side, with `static styles` serialised and `defer-hydration` ordering, is a different and much larger thing that this does not attempt.
> - **`Fable.Lit.React` renders again on a current React.** The directive called `ReactDom.render`, which React 18 deprecated and React 19 removed, so the interop raised rather than rendering for anyone on a supported React. It now creates a root through `react-dom/client` and renders into that, unmounting it when the element it was created for is replaced, which the old code never did. Requires React 18 or newer as a result, and the repo installs 19. It is checked by hand rather than in the suite (`npm run check:react`, and the file says why).
> - **New: a way to tell an island it has gone.** lit's `render` returns the root part, through which a rendered tree is told it is disconnected; the binding threw it away, so markup rendered into a plain element had no lifecycle at all — take the element off the page and its timers, sockets and observers carry on, because nothing was ever told. `Lit.renderPart` hands the part back, and `Program.stop`/`stopOn` uses it: subscriptions stopped, dispatch loop closed, AsyncDirectives notified. Components never had this problem — `Hook.useEffectOnce` and `disconnectedCallback` already cover them.
> - **New: typed custom events.** `customEvent<'T> "name"` states an event's name and the type of its `detail` in one value, `target.dispatchCustom(ev, payload)` raises it on any `EventTarget` (`document` included, which is what islands talking to each other need), and `Hook.useEventListener(target, ev, handler)` hands the handler its payload already typed. Change the payload and both ends stop compiling, rather than one end quietly reading a shape that is no longer there.
>
> One packaging note: `Fable.Lit.Unofficial` declares `@lit-labs/ssr-client` alongside `lit`, because `Hydrate.fs` imports it at module level and `Lit.Elmish` imports `Hydrate` — so a bundler has to resolve it even in an app that never hydrates. It is a dev dependency in practice: nothing of it reaches the output unless `adopt` is called, since the import resolves and then tree-shaking removes it.
>
> None of this has been offered upstream yet. If it is, and it lands, these packages get deprecated on NuGet pointing at the originals.

Fable.Lit is a collection of tools to help you write [Fable](https://fable.io/) apps by embedding HTML code into your F# code with the power of [Lit](https://lit.dev/).

Before doing anything make sure to install the dependencies after cloning the repository by running:

`npm install`

## How to test locally ?

`npm run test`

## How to publish a new version of the package ?

`npm run publish`

## How to work on the documentation ?

1. `npm run docs -- watch`
2. Go to [http://localhost:8080/](http://localhost:8080/)

## How to update the documentation ?

Deployment should be done automatically when pushing to `main` branch.

If the CI is broken, you can manually deploy it by running `npm run docs:deploy`.
