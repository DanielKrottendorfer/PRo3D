﻿---
tags: PRo3D Read.me
---

[PRo3D Homepage](http://pro3d.space) | [Aardvark Platform Repository](https://github.com/aardvark-platform)

![](http://www.pro3d.space/images/garden.jpg)

**PRo3D**, short for **P**lanetary **Ro**botics **3D** Viewer, is an interactive 3D visualization tool allowing planetary scientists to work with high-resolution 3D reconstructions of the Martian surface.

# Who uses PRo3D?

PRo3D aims to support planetary scientists in the course of NASA's and ESA's missions to find signs of life on the red planet by exploring high-resolution 3D surface reconstructions from orbiter and rover cameras.

Planetary geology is the most elaborately supported use-case of PRo3D, however we strive to expand our user groups to other use-cases, so we have also developed features for supporting science goals in **landing site selection** and **mission planning**.

# Features

* Geological analysis of 3D digital outcrop models
* Large data visualization
* Overlaying of arbitrary 3D surfaces

# Technology

PRo3D is based on the functional-first libraries of the [The Aardvark Platform](https://aardvarkians.com/). In december, we will finish the final bits for mac os finally making the application fully cross-platform.

# Getting started from pre-built binaries

Demo data and the pre-built application can be found on our release page: [TODO](TODO).

A video-based introduction to PRo3D can be found in the [Getting Started](http://www.pro3d.space/#started) of [PRo3D.space](http://www.pro3d.space)

# Getting started with from source

* clone
* run `build.cmd`
* `dotnet run PRo3D.Viewer`

# Packages

package | description
:-- | --- |
`pro3d.base` | serialization, cootrafo, c++ interop |
`pro3d.core` | Surfaces, Navigation, Annotations, Grouping, Scene Management, Bookmarks, Viewconfig |
`pro3d.viewer` | View Management / App State, GUI, Docking |

# How to contribute?

* what contributions are wanted?
  * Documentation
  * Feedback and Bug Reports
  * Improvement of existing code
  * Adding new features
* Opening Issues
* Opening Pull Requests

:question: write separate contribution doc

# Embedding in the Aardvark Platform

package | description | repo
:-- | --- | --- |
`pro3d.base` | serialization, cootrafo, c++ interop |
`pro3d.core` | Surfaces, Navigation, Annotations, Grouping, Scene Management, Bookmarks, Viewconfig |
`pro3d.viewer` | View Management / App State, GUI, Docking |

# Code of Conduct

We employ the Contributor Covenant Code of Conduct. Read more [here](./CODE_OF_CONDUCT.md)

