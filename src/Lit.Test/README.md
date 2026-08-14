# Fable.Lit.Test.Unofficial

Testing helpers for Fable.Lit, for use with [@web/test-runner](https://modern-web.dev/docs/test-runner/overview/).

```
dotnet add package Fable.Lit.Test.Unofficial
```

```fsharp
open Lit.Test

it "counts up" <| fun () -> promise {
    use! el = render_html $"<my-counter></my-counter>"
    do! click el.El (el.El.getButton "increment")
    el.El.getByText "value" |> Expect.innerText "Value: 1"
}
```

`render` mounts a `TemplateResult`, `render_html` mounts markup by tag, `elementUpdated`
waits for a component to finish rendering, and `Program.mountAndTest` runs an Elmish
program in a container so a test can drive it through its own messages.

## Why "Unofficial"

This is a republished build of [Fable.Lit](https://github.com/fable-compiler/Fable.Lit) by
Alfonso García-Caro Núñez and its contributors, packaged from
[a fork](https://github.com/OnurGumus/Fable.Lit) so that fixes can be used before they land
upstream. The toolchain is .NET 10, Fable 5, Fable.Core 4 and lit 3.

Reference this **or** upstream Fable.Lit, never both: they share the `Lit` namespace.
