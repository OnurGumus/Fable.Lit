# Fable.Lit.Feliz.Unofficial

A [Feliz](https://github.com/Zaid-Ajaj/Feliz)-style API for Fable.Lit.

```
dotnet add package Fable.Lit.Feliz.Unofficial
```

For templates written as F# values rather than interpolated strings:

```fsharp
open Lit.Feliz

Feliz.toLit (Html.div [ Attr.className "card"; Html.p [ Feliz.text "hello" ] ])
```

Everything the interpolated `html $"..."` form can express is available here; the two
produce the same `TemplateResult` and can be nested in each other.

`Feliz.Engine` has only ever published prereleases, so this package carries a prerelease
dependency (NuGet warns NU5104). Restore handles it.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
