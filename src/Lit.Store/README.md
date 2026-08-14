# Fable.LitStore.Unofficial

Read a [Fable.Store](https://github.com/davedawkins/Fable.Store) from a Fable.Lit component.

```
dotnet add package Fable.LitStore.Unofficial
```

```fsharp
open Lit
open LitStore

[<HookComponent>]
let ThemeBadge () =
    let theme = Hook.useStore Theme.store
    html $"""<span>{Theme.name theme}</span>"""
```

The subscription is taken in the component's initialiser, so the first render already has
the store's value, and is disposed when the component disconnects. An element that is
*moved* rather than removed resubscribes and catches up on what changed while it was out
of the document.

Writing is not a hook: `dispatch` from `Store.makeElmish`, or `store.Update`, is the same
function for every caller and has nothing per-component about it.

Worth knowing before a component is a store's only reader: **Fable.Store disposes a store
when its last subscriber leaves.** A store meant to outlive its readers needs something
else holding a subscription — for a page-level concern, one taken where the app starts.

### Why this exists

The original [Fable.LitStore](https://github.com/davedawkins/Fable.Store) depends on
upstream Fable.Lit, and both live in the `Lit` namespace, so it cannot be referenced
alongside this fork. The implementation here is written fresh against the same signature,
`Hook.useStore store`, so moving between the two is a package reference and nothing else.

`Fable.Store` has only ever published prereleases, so this package carries a prerelease
dependency (NuGet warns NU5104). Restore handles it; nothing else is required of you.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
