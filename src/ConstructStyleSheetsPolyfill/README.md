# ConstructStyleSheetsPolyfill.Unofficial

Fable binding for [construct-style-sheets-polyfill](https://github.com/calebdwilliams/construct-style-sheets).

```
dotnet add package ConstructStyleSheetsPolyfill.Unofficial
```

Constructable stylesheets are what lit uses to share styles between instances of a
component. Browsers that lack them need the polyfill loaded before any component is
defined:

```fsharp
ConstructStyleSheetsPolyfill.register ()
```

Referencing the package is not enough on its own — a bundler drops a module nothing
imports, so the call is the reference.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
