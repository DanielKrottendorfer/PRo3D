![Windows](https://github.com/vrvis/PRo3D/workflows/Windows/badge.svg)![Linux](https://github.com/vrvis/PRo3D/workflows/Linux/badge.svg)![Mac OS](https://github.com/vrvis/PRo3D/workflows/MacOS/badge.svg)

![](http://www.pro3d.space/images/garden.jpg)

**PRo3D**, short for **P**lanetary **Ro**botics **3D** Viewer, is an interactive 3D visualization tool allowing planetary scientists to work with high-resolution 3D reconstructions of the Martian surface. Additional information can be found on the [PRo3D Homepage](http://pro3d.space).


# Who uses PRo3D?

PRo3D aims to support planetary scientists in the course of NASA's and ESA's missions to find signs of life on the red planet by exploring high-resolution 3D surface reconstructions from orbiter and rover cameras.

Planetary geology is the most elaborately supported use-case of PRo3D, however we strive to expand our user groups to other use-cases, so we have also developed features for supporting science goals in **landing site selection** and **mission planning**.

# Features

* Geological analysis of 3D digital outcrop models
* Large data visualization
* Overlaying of arbitrary 3D surfaces

# Licensing

PRo3D is **free** for academic use. When used for publications, we kindly ask to reference PRo3D and [PRo3D Homepage](http://pro3d.space). For commercial use, and or customization, please contact science@vrvis.at.

# Technology & System Requirements

PRo3D is based on the functional-first libraries of the [The Aardvark Platform](https://aardvarkians.com/), available on [github](https://github.com/aardvark-platform). In December, we will finish the final bits for mac os finally making the application fully cross-platform.

_required:_

CPU: Intel i5 or AMD equivalent
GPU: dedicated GPU, NVIDIA Geforce 700s Series or greater
RAM: 8 GB

_recommended:_

CPU: Intel i7 or AMD equivalent
GPU: NVIDIA Geforce 1650GTX or AMD equivalent
RAM: 16GB

_technological constraints:_

OS/Runtime: Windows 10 (64bit, v10.0.17763), .NET Core 3.1 (linux and mac versions are in development)
Graphics: NVIDIA Kepler Architecture (GTX 6*) or greater

PRo3D's performance may vary with the size and type of datasets and the selected quality settings for surface rendering. PRo3D may as well run on machines beneath the required specification. Most of the time, PRo3D also runs on AMD cards, but it is not guaranteed.

# Getting started from pre-built binaries

Demo data and the pre-built application versions can be found on our [PRo3D Release Page](https://hackmd.io/@A2KmRrUKS4qQoCFA51Eocw/BJzMnBzoP). A video-based introduction to PRo3D can be found in the [Getting Started](http://www.pro3d.space/#started) section of [PRo3D.space](http://www.pro3d.space)

# Getting started with from source

for contributions and when compiling from source windows is the recommended platform. Currently we use few native libraries which are not yet compiled for mac/linux delaying full cross-platform support.

* install [dotnet 3.1 sdk](https://dotnet.microsoft.com/download)
* `git clone git@github.com:vrvis/PRo3D.git`
* run `build.cmd`
* `dotnet run PRo3D.Viewer` or open `/src/PRo3D.sln` with Visual Studio 2019

A reconstruction of the Cape Desire outcrop at the rim of Victoria crater can be found [here](http://download.vrvis.at/realtime/PRo3D/CapeDesire/Cape_Desire_RGB.zip). For loading the data please watch the video-based introduction to PRo3D can be found in the [Getting Started](http://www.pro3d.space/#started) section of [PRo3D.space](http://www.pro3d.space)

> Image data courtesy NASA/JPL/CalTech/ASU, 3D data processing by JOANNEUM RESEARCH under ESA/PRODEX Contracts PEA 4000105568 & 4000117520. The research leading to these results has received funding from the European Community’s Seventh Framework Programme (FP7/2007-2013) under grant agreement n° 312377 PRoViDE

If you have any questions, feel free to contact us on [discord](https://discord.gg/CyxNwrg).

# Packages

package | description
:-- | --- |
`pro3d.base` | serialization, cootrafo, c++ interop |
`pro3d.core` | Surfaces, Navigation, Annotations, Grouping, Scene Management, Bookmarks, Viewconfig |
`pro3d.viewer` | View Management / App State, GUI, Docking |

# How to contribute?

If you want to contribute, feel free to contact us on [discord](https://discord.gg/CyxNwrg).

# Embedding in the Aardvark Platform

package | description | repo
:-- | --- | --- |
`pro3d.base` | serialization, cootrafo, c++ interop |
`pro3d.core` | Surfaces, Navigation, Annotations, Grouping, Scene Management, Bookmarks, Viewconfig |
`pro3d.viewer` | View Management / App State, GUI, Docking |

# Code of Conduct

We employ the Contributor Covenant Code of Conduct. Read more [here](./CODE_OF_CONDUCT.md)

