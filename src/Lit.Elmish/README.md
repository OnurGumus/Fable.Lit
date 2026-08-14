# Fable.Lit.Elmish.Unofficial

[Elmish](https://elmish.github.io) loops driving lit templates.

```
dotnet add package Fable.Lit.Elmish.Unofficial
```

An Elmish program that renders into an element:

```fsharp
open Lit.Elmish

Program.mkProgram init update view
|> Program.withLit "app"
|> Program.run
```

Or a loop that belongs to one component, started and kept by it:

```fsharp
[<HookComponent>]
let Counter () =
    let model, dispatch = Hook.useElmish (init, update)
    view model dispatch
```

### Beyond upstream

- `Program.withLitHydrated` *adopts* markup that was rendered on the server by
  `Lit.Server.Unofficial` rather than replacing it, and `Program.withLitHydratedInShadowRoot`
  does the same inside a declarative shadow root.
- Mounting twice on one element hands over instead of colliding, which is what a hot
  update is: the new program starts from the model the old one had reached.
- `Program.stopOn` stops a program mounted on an element, disposing its subscriptions and
  closing the loop, so a dispatch afterwards cannot render into an element nobody is
  looking at.

Note that `Hook.useElmish` does not stop its program when the component disconnects: the
model is disposed if it happens to be `IDisposable`, and nothing else. Subscriptions in a
component outlive it unless you tear them down yourself, because disconnection cannot tell
being *moved* from being *removed*.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
