---
name: Fable.Lit
last_commit_released: b08eb88bc057f5c8eb5f1cf40f87cf87b48b909b
force_version: 2.0.0
updaters:
  - regex:
      file: Lit.fsproj
      pattern: (?<=<Version>).*(?=</Version>)
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 1.4.2 - 2022-07-14

* `Lit.mapiUnique` @leolorenzoluis
* `styles` binding @AngelMunoz
* Fix #39: Better error message for unsafe CSS
* Fix #31: Conflict with `name` attribute

## 1.4.1 - 2021-11-23

* Add Hook.use_scoped_css
* Hook.useTransition now gives class name instead of css
* Make Hook.useHmr work with multiple components

## 1.3.2 - 2021-11-13

* HMR improvements

## 1.3.1 - 2021-11-04

* Fix `useEffectOnce` running twice on LitElements

## 1.3.0 - 2021-10-11

* Remove custom useElmish implementation
* Fix #22: Add renderRoot to LitElement

## 1.2.0 - 2021-10-03

* Add ofImport
* Clean up API

## 1.1.0 - 2021-10-01

* useShadowDOM setting for LitElements
* HMR support for LitElement

## 1.0.0 - 2021-09-27

* Stable release
