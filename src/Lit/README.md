# Fable.Lit.Unofficial

F# bindings for [lit](https://lit.dev).

```
dotnet add package Fable.Lit.Unofficial
```

Write templates in F# with an interpolated string, and render them with lit:

```fsharp
open Lit

let view name =
    html $"""<p>Hello {name}, <button @click={Ev(fun _ -> printfn "hi")}>click</button></p>"""

Lit.render (document.getElementById "app") (view "world")
```

Components come in two shapes. A `HookComponent` is a function that renders into whoever
called it; a `LitElement` is a real custom element with its own tag, shadow root and styles:

```fsharp
[<LitElement("my-counter")>]
let Counter () =
    LitElement.init (fun config -> config.styles <- [ css $"p {{ margin: 0; }}" ]) |> ignore
    let count, setCount = Hook.useState 0
    html $"""<p>{count}</p><button @click={Ev(fun _ -> setCount (count + 1))}>+</button>"""
```

### Beyond upstream

- `Lit.renderPart` returns the root part, so a tree rendered into a plain element can be
  told it was disconnected (`part.setConnected false`) instead of running forever.
- `Lit.trackConnection` upgrades any dashed tag into a host that reports joining and
  leaving the document, shaped as an effect: what runs on arrival returns what to undo on
  departure.
- `Hydrate.adopt` takes over server-rendered markup, and renders instead of failing when
  the markup cannot be adopted — including the refusal lit reports only to the console.
- `config.formAssociated` makes a `LitElement` visible to a form, so `attachInternals`
  works and a custom control can contribute a value to submission.
- Elements that are *moved* rather than removed keep working: reconnecting re-establishes
  what `useEffectOnce` set up, which upstream disposes and never restores.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
