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

There is a worked example at
[LitHydrationDemo](https://github.com/OnurGumus/LitHydrationDemo).

This package has no Fable dependency: it is ordinary .NET, referenced by the server project.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
