---
name: Fable.Lit.Elmish
last_commit_released: 60a1f8e74ea8b862eb56a2477f84fc563847aaa5
force_version: 2.0.0
depends_on:
  - ../Lit/
updaters:
  - regex:
      file: Lit.Elmish.fsproj
      pattern: (?<=<Version>).*(?=</Version>)
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 1.4.0 - 2021-11-13

* Remove HMR support in favor of Fable.Elmish.HMR

## 1.3.0 - 2021-10-11

* Implement useElmish using Elmish.Program

## 1.2.0 - 2021-10-03

* Add Hook.useElmish(program)

## 1.0.0 - 2021-09-27

* Stable release
