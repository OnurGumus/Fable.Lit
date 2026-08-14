# Fable.Lit.React.Unofficial

Use React components inside lit templates, and lit templates inside React.

```
dotnet add package Fable.Lit.React.Unofficial
```

```fsharp
open Lit.React

// A React component, rendered by a lit template.
let myWidget = React.toLit (MyReactComponent)
html $"""<div>{myWidget {| title = "hello" |}}</div>"""

// A lit template, rendered by React.
React.ofLit (html $"""<p>from lit</p>""")
```

### Beyond upstream

Mounting goes through `createRoot` from `react-dom/client`, so this works on React 18 and
19. Upstream calls `ReactDom.render`, which those versions removed.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
