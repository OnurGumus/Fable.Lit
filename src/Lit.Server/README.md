# Lit.Server.Unofficial

Render Fable.Lit templates to HTML on .NET — no Node, no `@lit-labs/ssr`.

```
dotnet add package Lit.Server.Unofficial
```

The same `html $"..."` view compiles to lit in the browser and to HTML on the server,
because both sides resolve `open Lit` — one against this package, the other against
`Fable.Lit.Unofficial`. No conditional compilation, no template written twice.

```fsharp
open Lit
open Lit.Server

let counter, _ = Views.Counter.init ()

// Plain HTML.
Server.render (Views.Counter.view counter ignore)

// HTML carrying lit's hydration markers, so the browser can adopt it
// with Hydrate.adopt or Program.withLitHydrated.
Server.renderHydratable (Views.Counter.view counter ignore)
```

`Server.toNode` and `Server.toHydratableNode` return a node rather than a string, for
composing into [HtmlTypeProvider](https://github.com/OnurGumus/HtmlTypeProvider) templates
without anything being escaped twice. `Server.toShadowRootNode` emits
`<template shadowrootmode="open">` with styles inside, which the parser attaches as a
shadow root while it reads the page.

Event handlers are dropped on the server — a closure cannot be serialised — and become
real listeners the moment lit adopts the markup.

## A component's shadow root

A `LitElement` can arrive rendered as well. Keep what it draws where both sides reach it,
write that into the component's own tag, and switch adoption on in the browser:

```fsharp
// Shared: compiled by both.
module Badge =
    let styles = ":host { display: block }"

    let view (count: int) (bump: unit -> unit) =
        html $"""<button @click={Ev(fun _ -> bump ())}>{count}</button>"""

// Server: what goes inside <my-badge></my-badge> in the page.
Server.toShadowRootNode Badge.styles (Badge.view 0 ignore)

// Browser: once, at startup.
Hydrate.elements ()

[<LitElement("my-badge")>]
let MyBadge () =
    LitElement.init (fun config -> config.styles <- [ Lit.unsafeCSS Badge.styles ]) |> ignore
    let count, setCount = Hook.useState 0
    Badge.view count (fun () -> setCount (count + 1))
```

The component must render, first time, what the server rendered: the same view from the
same data. Without `Hydrate.elements ()` it draws a second copy beside the server's, and
says so in the console.

### Inside a view

`toShadowRootNode` fills a hole in a page template. A component that sits inside an
island's view, or inside another component's, has a view around it instead, and
`Lit.shadowRoot` is the same thing where a view can reach it:

```fsharp
// Added to the Badge module above: what the server draws for a badge inside a view.
let drawn (count: int) = Lit.shadowRoot styles (view count ignore)

// A view with a badge in it.
let view model dispatch =
    html $"""<section>
               <my-badge>{Badge.drawn model.Count}</my-badge>
               <button @click={Ev(fun _ -> dispatch Reset)}>reset</button>
             </section>"""
```

On the server it writes the component's shadow root into its tag. In the browser it is
`Lit.nothing`, because there a component draws its own root. It has to come first inside
the element, which is the element the parser attaches the root to.

The styles and the view are paired once, in the component's own module, rather than in
each view that uses it. `Lit.Server` does not work out a component's stylesheet, so that
pairing is the one place the two are kept from disagreeing.

If the view hands the component a property (`.count={model.Count}`), the property is not
in the HTML, so the component is marked `defer-hydration` and waits: hydrating the view
takes the mark off and sets the property in the same pass.

## What it refuses

Anything it cannot render the way lit would raises `UnsupportedTemplateValue` rather than
guessing. Two of those are easy to reach for:

- A `Node` in a hole. A view is the same code the browser runs, and there is no `Node`
  there. For a shadow root inside a view there is `Lit.shadowRoot`; anything else
  composes the other way round, the template's `Node` into the page's hole.
- A `<template>` element in markup lit is going to adopt. lit never looks inside one, so
  every binding after it would silently never be made. Plain `render` and `toNode` write
  it as it stands.

There is a worked example at
[LitHydrationDemo](https://github.com/OnurGumus/LitHydrationDemo).

This package has no Fable dependency: it is ordinary .NET, referenced by the server project.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
