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
> Each of these is offered upstream as a PR. If they land, these packages get deprecated on NuGet pointing at the originals.

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
