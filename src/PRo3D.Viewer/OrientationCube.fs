namespace PRo3D.OrientationCube

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.Rendering
open Aardvark.SceneGraph

open Aardvark.UI
open Aardvark.UI.Trafos
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Core

//todo move to base

module Shader =
    
    open FShade
    
    let aspectTrafo (v : Effects.Vertex) =
        vertex {
            let vps = uniform.ViewportSize
            let aspect = (float vps.X) / (float vps.Y)
            let tx = 0.75
            let ty = 0.75
            return {v with pos = V4d(v.pos.X / aspect + tx, v.pos.Y + ty, v.pos.Z, v.pos.W)}
        }
    
    let samplerAniso =
        sampler2d {
            texture uniform.DiffuseColorTexture
            filter Filter.Anisotropic
        }

    let anisoTexShader (v : Effects.Vertex) =
        fragment {
            let c = samplerAniso.Sample v.tc
            return {v with c = c}
        }

    let screenSpaceScaleTrafo (v : Effects.Vertex) =
        vertex {
            let loc = uniform.CameraLocation
            let hvp = float uniform.ViewportSize.X

            //let mt = uniform?modelTrafo
            let ct : M44d = uniform?posTrafo

            let p    : V4d   = uniform?WorldPos
            let size : float = uniform?Size

            let t = ct*p
            let dist = (V3d(t.X, t.Y, t.Z) - loc).Length      
            let scale = dist * size / hvp
    
            return { 
              v with
                pos = V4d(v.pos.X * scale, v.pos.Y * scale, v.pos.Z * scale, v.pos.W)
            }
        }
        
module Sg =    
    
    let loadCubeModel (filename : string) =
        Aardvark.SceneGraph.IO.Loader.Assimp.load filename
        |> Sg.adapter
        |> Sg.noEvents
    
    let orthoOrientation (camView : aval<CameraView>) (refSys:AdaptiveReferenceSystem) (model : ISg<'msg>) = // (model:ISg<obj>) = //
        let viewTrafo =
            camView
            |> AVal.map ( fun cv ->
                let view = CameraView.look V3d.OOO cv.Forward cv.Sky
                view.ViewTrafo
            )
       
        let orthoTrafo =
            let d = 3.0
            //let t = V3d((-d+1.0), -d+1.0, 0.0)
            let min = V3d(-d, -d, -d*2.0)
            let max = V3d(d, d, d*2.0)
            let fr = Frustum.ortho (Box3d(min, max))
            AVal.constant (Frustum.orthoTrafo fr)

        let northAngle = 
            adaptive {
                let! cam = camView
                let! n = refSys.north.value
                let! offset = refSys.noffset.value
                let Vn = (Vec.Cross(cam.Forward.Normalized, n.OYO)).Normalized
                //((Va x Vb) . Vn) / (Va . Vb)
                let angle = (Vec.Dot((Vec.Cross(cam.Forward.Normalized, n.OYO)), Vn)) / (Vec.Dot(cam.Forward.Normalized, n.OYO)) //.OYO
                //return angle + offset
                let nangle = (angle + offset) % 360.0
                return nangle //(nangle + 360.0) % 360.0 
            }
   

        model
        |> Sg.trafo (AVal.constant (Trafo3d.Scale(0.5,0.5, -0.5)))
        |> Sg.trafo (AVal.constant (Trafo3d.RotationXInDegrees(90.0)))
        |> Sg.trafo (refSys.up.value |> AVal.map ( fun u ->  Trafo3d.RotateInto(V3d.ZAxis, u)))
        |> Sg.trafo (northAngle |> AVal.map ( fun n -> (Trafo3d.RotationXInDegrees(n))))
        |> Sg.viewTrafo viewTrafo
        |> Sg.projTrafo orthoTrafo
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! Shader.aspectTrafo
            do! Shader.anisoTexShader
        }
        |> Sg.pass (RenderPass.after "cube" RenderPassOrder.Arbitrary RenderPass.main)
    
    let insideOrientation (camView : aval<CameraView>) (frustum : aval<Frustum>) (model : ISg<'msg>) =
        let viewTrafo =
            camView
            |> AVal.map ( fun cv ->
                let view = CameraView.look V3d.OOO cv.Forward V3d.OOI
                view.ViewTrafo
            )
        
        let perspTrafo = frustum |> AVal.map ( fun f -> Frustum.projTrafo f)
        
        model
        |> Sg.trafo (AVal.constant (Trafo3d.Scale(100.0, 100.0, 100.0)))
        |> Sg.viewTrafo viewTrafo
        |> Sg.projTrafo perspTrafo
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! DefaultSurfaces.diffuseTexture
        }
        |> Sg.pass (RenderPass.after "cube" RenderPassOrder.Arbitrary RenderPass.main)

    //let view (camView : aval<CameraView>) (config:AdaptiveViewConfigModel) (refSys:AdaptiveReferenceSystem) =
    //    aset {
    //        let! draw = config.drawOrientationCube
    //        yield match draw with
    //                | true ->  loadCubeModel "../../data/rotationcube/rotationcube.dae"
    //                            |> orthoOrientation camView refSys
    //                |_-> Sg.empty
    //        }  |> Sg.set