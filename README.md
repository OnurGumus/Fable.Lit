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
> - **New: typed custom events.** `customEvent<'T> "name"` states an event's name and the type of its `detail` in one value, `target.dispatchCustom(ev, payload)` raises it on any `EventTarget` (`document` included, which is what islands talking to each other need), and `Hook.useEventListener(target, ev, handler)` hands the handler its payload already typed. Change the payload and both ends stop compiling, rather than one end quietly reading a shape that is no longer there.
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
