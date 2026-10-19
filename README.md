# Fable.Lit

Fable.Lit is a collection of tools to help you write [Fable](https://fable.io/) apps by embedding HTML code into your F# code with the power of [Lit](https://lit.dev/).

Before doing anything make sure to install the dependencies after cloning the repository by running:

`npm install`

## How to test locally ?

`npm run test`

## How to publish a new version of the package ?

Releases are made by [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt) from [Conventional Commits](https://www.conventionalcommits.org/), so pull requests are squash-merged and their title becomes the commit message (`fix: ...`, `feat: ...`).

1. After each push to `main` that passes the tests, ShipIt opens (or updates) a `chore: release ...` pull request with the new versions and `CHANGELOG.md` entries.
2. Merging that pull request publishes the packages to NuGet.

Only the packages with a `CHANGELOG.md` are released: Fable.Lit and Fable.Lit.Elmish.

To preview the next release locally: `dotnet shipit --mode local --skip-invalid-commit`, then `git restore` the changed files.

## How to work on the documentation ?

1. `npm run docs -- watch`
2. Go to [http://localhost:8080/](http://localhost:8080/)

## How to update the documentation ?

Run the "Publish" workflow from the Actions tab, or deploy manually by running `npm run docs:deploy`.
