namespace PRo3D

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Base.Rendering    

open Aardvark.Application
    
open Aardvark.UI

    
open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.SceneGraph.FShadeSceneGraph
open Aardvark.VRVis.Opc

open PRo3D.ReferenceSystem    
open PRo3D.Base
open PRo3D.Base.Annotation

open Aether
open Aether.Operators
open Aardvark.UI.Primitives

module ReferenceSystemApp =

    //open Aardvark.UI.ChoiceModule
   
    type Action =
        | InferCoordSystem   of V3d
        | UpdateUpNorth      of V3d
        | SetUp              of Vector3d.Action
        | SetNorth           of Vector3d.Action
        | SetNOffset         of Numeric.Action
        | ToggleVisible
        | SetScale           of string
        | SetArrowSize       of double
        | SetPlanet          of Planet

    type InnerConfig<'a> =
        {
            arrowLength     : Lens<'a,float>
            arrowThickness  : Lens<'a,float>            
        } 

    type MInnerConfig<'ma> =
        {
            getArrowLength    : 'ma -> aval<float>
            getArrowThickness : 'ma -> aval<float>
            getNearDistance   : 'ma -> aval<float>
        }

    let updateVectorInDegree (up:V3d) (point:V3d) (origin:V3d) (theta:float) =
        //https://sites.google.com/site/glennmurray/Home/rotation-matrices-and-formulas
        
        let xyPlane = new Plane3d(up.Normalized, origin) 

        let u = xyPlane.Normal.X //up.X
        let v = xyPlane.Normal.Y //up.Y
        let w = xyPlane.Normal.Z //up.Z
        let a = xyPlane.Point.X //pos.X
        let b = xyPlane.Point.Y //pos.Y
        let c = xyPlane.Point.Z //pos.Z
        let x = point.X
        let y = point.Y
        let z = point.Z
        
        let t = theta.RadiansFromDegrees()

        let u2 = u*u
        let v2 = v*v
        let w2 = w*w
        let cosT = Math.Cos(t)
        let oneMinusCosT = 1.0-cosT
        let sinT = Math.Sin(t)
        
        // Build the matrix entries element by element.
        let m11 = u2 + (v2 + w2) * cosT
        let m12 = u*v * oneMinusCosT - w*sinT
        let m13 = u*w * oneMinusCosT + v*sinT
        let m14 = (a*(v2 + w2) - u*(b*v + c*w))*oneMinusCosT
                + (b*w - c*v)*sinT

        let m21 = u*v * oneMinusCosT + w*sinT
        let m22 = v2 + (u2 + w2) * cosT
        let m23 = v*w * oneMinusCosT - u*sinT
        let m24 = (b*(u2 + w2) - v*(a*u + c*w))*oneMinusCosT
                + (c*u - a*w)*sinT

        let m31 = u*w * oneMinusCosT - v*sinT
        let m32 = v*w * oneMinusCosT + u*sinT
        let m33 = w2 + (u2 + v2) * cosT
        let m34 = (c*(u2 + v2) - w*(a*u + b*v))*oneMinusCosT
                + (a*v - b*u)*sinT
                
        let px = m11*x + m12*y + m13*z + m14
        let py = m21*x + m22*y + m23*z + m24
        let pz = m31*x + m32*y + m33*z + m34

        V3d(px, py, pz)
    
   
    let upVector (point:V3d) (planet) = CooTransformation.getUpVector point planet //point.Normalized
    
    let northVector (up:V3d) =
        let east = V3d.OOI.Cross(up)
        up.Cross(east)
    
    let updateCoordSystem (p:V3d) (planet:Planet) (model : ReferenceSystem) = 
        let up = upVector p planet
        let n  = match planet with | Planet.None -> V3d.IOO | _ -> northVector up
        let no = Rot3d.Rotation(up, model.noffset.value |> Double.radiansFromDegrees).Transform(n) //updateVectorInDegree up n model.origin model.noffset.value 
        { model with north = ReferenceSystem.setV3d n; up = ReferenceSystem.setV3d up; northO = no }

   
    let update<'a> (bigConfig : 'a) (config : ReferenceSystemConfig<'a>) (model : ReferenceSystem) (act : Action) =
        match act with
            | InferCoordSystem p ->
                let m' = updateCoordSystem p model.planet model
                { m' with origin = p }, bigConfig
            | UpdateUpNorth p ->
                updateCoordSystem p model.planet model, bigConfig
            | SetUp up ->    
                let up = Vector3d.update model.up up
                let up = ReferenceSystem.setV3d up.value.Normalized
                { model with up =  up}, bigConfig
            | SetNorth  n     -> 
                let n = Vector3d.update model.north n
                let n = ReferenceSystem.setV3d n.value//.Normalized
                
                { model with north = n }, bigConfig
            | SetNOffset o ->
                let noffset = Numeric.update model.noffset o
                let no = 
                  Rot3d.Rotation(model.up.value, noffset.value |> Double.radiansFromDegrees)
                    .Transform(model.north.value) |> Vec.normalize                


                { model with noffset = noffset; northO = no }, bigConfig 
            | ToggleVisible   -> 
                { model with isVisible = not model.isVisible}, bigConfig
            | SetArrowSize d  ->
                let big' = Optic.set config.arrowLength d bigConfig
                model, big'          
            | SetScale s ->
                { model with selectedScale = s }, bigConfig
            | SetPlanet p ->      
                let m' = updateCoordSystem model.origin p model
                { m' with planet = p }, bigConfig

    module Sg =
        open PRo3D.Base.Sg

        type MarkerStyle = {
                position  : aval<V3d>
                direction : aval<V3d>
                color     : aval<C4b>
                size      : aval<float>
                thickness : aval<float>
                hasArrow  : aval<bool>
                text      : aval<option<string>>
                fix       : aval<bool>
            }

        //let coneISg color radius size trafo =  
        //    Sg.cone 30 color radius size
        //            |> Sg.noEvents
        //            |> Aardvark.UI.FShadeSceneGraph.Sg.shader {
        //                do! Aardvark.GeoSpatial.Opc.Shader.stableTrafo
        //                do! DefaultSurfaces.vertexColor
        //               // do! DefaultSurfaces.simpleLighting
        //            }
        //            |> Sg.trafo(trafo)

        let directionMarker (near:aval<float>) (cam:aval<CameraView>) (style : MarkerStyle) =
            aset{
               
                //let! dirs = style.direction
                
                let lineLengthScale = style.size                    
                let scaledLength = 
                    AVal.map2(fun (str:V3d) s -> str.Normalized * s) style.direction lineLengthScale

                //let scaledLength = dir * length
                let al = 
                    alist {
                        let! p = style.position
                        let! length = scaledLength
                        yield p
                        yield p + length
                    }

                let posTrafo =
                    style.position |> AVal.map(fun d -> Trafo3d.Translation(d))

                //let coneTrafo = 
                //    AVal.map2(fun p s -> Trafo3d.RotateInto(V3d.ZAxis, dirs.Normalized) * Trafo3d.Translation(p + s)) style.position scaledLength

                //let radius = length * 0.1
                //let scale =  DrawingApp.Sg.computeInvariantScale cam p 5.0
                //let radius = lineLengthScale |> AVal.map(fun d -> d * 0.04)
                //let coneSize = lineLengthScale |> AVal.map(fun d -> d * 0.3)

                let nLabelPos = AVal.map2(fun l r -> l + r) style.position scaledLength
                let nPosTrafo =
                    nLabelPos |> AVal.map(fun d -> Trafo3d.Translation(d))

                
                let label = 
                    style.text 
                      |> AVal.map(fun x ->
                        match x with 
                          | Some text -> Sg.text cam near ~~60.0 nLabelPos nPosTrafo ~~0.05 ~~text
                          | None -> Sg.empty)

                yield Sg.drawLines al (AVal.constant 0.0)style.color style.thickness posTrafo
                yield label |> Sg.dynamic                                   
                 
            } |> Sg.set 

           
        let point (pos:aval<V3d>) (color:aval<C4b>) (cam:aval<CameraView>) = 
            Sg.dot color (AVal.constant 3.0)  pos

//["1km";"100m";"10m";"1m";"10cm";"1cm";"1mm";"0.1mm"] 
        let scaleToSize (a:string) =
            match a with
              | "100km" -> 100000.0
              | "10km"  ->  10000.0
              | "1km"   ->   1000.0
              | "100m"  ->    100.0
              | "10m"   ->     10.0
              | "2m"    ->      2.0
              | "1m"    ->      1.0
              | "10cm"  ->      0.1
              | "1cm"   ->      0.01
              | "1mm"   ->      0.001
              | "0.1mm" ->      0.0001
              | _       ->      1.0

        let getOrientationSystem (mbigConfig : 'ma) (minnerConfig : MInnerConfig<'ma>) (model:AdaptiveReferenceSystem) (cam:aval<CameraView>) =
            let thickness = AVal.constant 2.0
            let near      = minnerConfig.getNearDistance   mbigConfig

            let east = AVal.map2(fun (l:V3d) (r:V3d) -> r.Cross(l) ) model.up.value model.north.value

            let size = AVal.constant 2.0
            let posTrafo = AVal.constant (Trafo3d.Translation(V3d.OOO))
            let position = AVal.constant V3d.OIO

            let styleUp : MarkerStyle = {
                position  = position
                direction = model.up.value
                color     = AVal.constant C4b.Magenta
                size      = size
                thickness = thickness
                hasArrow  = AVal.constant false
                text      = AVal.constant None
                fix       = AVal.constant false
            }


            let upV = 
                alist {
                    let! udir = model.up.value
                    let! position = position
                    yield position
                    yield position + udir.Normalized
                    }
                    
            let northV = 
                alist {
                    let! ndir = model.north.value
                    let! position = position
                    yield position
                    yield position + ndir.Normalized
                    }

            let eastV = 
                alist {
                    let! edir = east
                    let! position = position
                    yield position
                    yield position + edir.Normalized
                    }

            let nLabelPos = AVal.map2(fun l r -> l + r) position model.north.value
            let nPosTrafo =nLabelPos |> AVal.map(fun d -> Trafo3d.Translation(d))
            let label = Sg.text cam near (AVal.constant 60.0) nLabelPos nPosTrafo (AVal.constant 0.05) (AVal.constant "N")

            Sg.ofList [
                point model.origin (AVal.constant C4b.Red) cam
                //sizeText
                Sg.drawLines upV (AVal.constant 0.0)(AVal.constant C4b.Blue) thickness posTrafo 
                styleUp |> directionMarker near cam 
                Sg.drawLines northV (AVal.constant 0.0)(AVal.constant C4b.Red) thickness posTrafo 
                Sg.drawLines eastV (AVal.constant 0.0)(AVal.constant C4b.Green) thickness posTrafo
                label                     
            ]   |> Sg.onOff(model.isVisible)       

        let view<'ma> (mbigConfig : 'ma) (minnerConfig : MInnerConfig<'ma>) (model:AdaptiveReferenceSystem) (cam:aval<CameraView>)  : ISg<Action> =
                       
            let length    = minnerConfig.getArrowLength    mbigConfig
            let thickness = minnerConfig.getArrowThickness mbigConfig
            let near      = minnerConfig.getNearDistance   mbigConfig

            let east = AVal.map2(fun (l:V3d) (r:V3d) -> r.Cross(l) ) model.up.value model.northO // model.north.value

            let size = model.selectedScale |> AVal.map scaleToSize

            let styleUp : MarkerStyle = {
                position  = model.origin
                direction = model.up.value
                color     = AVal.constant C4b.Blue
                size      = size
                thickness = thickness
                hasArrow  = AVal.constant false
                text      = AVal.constant None
                fix       = AVal.constant false
            }

            let styleNorth : MarkerStyle = {
                position  = model.origin
                direction = model.northO //model.north.value
                color     = AVal.constant C4b.Red
                size      = size
                thickness = thickness
                hasArrow  = AVal.constant true
                text      = AVal.constant (Some "N")
                fix       = AVal.constant false
            }

            let styleEast : MarkerStyle = {
                position  = model.origin
                direction = east
                color     = AVal.constant C4b.Green
                size      = size
                thickness = thickness
                hasArrow  = AVal.constant false
                text      = AVal.constant None
                fix       = AVal.constant false
            }

            let styleX : MarkerStyle = {
                position  = model.origin
                direction = AVal.constant V3d.IOO
                color     = AVal.constant C4b.Magenta
                size      = size
                thickness = thickness
                hasArrow  = AVal.constant false
                text      = AVal.constant (Some "X")
                fix       = AVal.constant false
            }

            let styleY : MarkerStyle = {
                position  = model.origin
                direction = AVal.constant V3d.OIO
                color     = AVal.constant C4b.Cyan
                size      = size
                thickness = thickness
                hasArrow  = AVal.constant false
                text      = AVal.constant (Some "Y")
                fix       = AVal.constant false
            }

            let styleZ : MarkerStyle = {
                position  = model.origin
                direction = AVal.constant V3d.OOI
                color     = AVal.constant C4b.Yellow
                size      = size
                thickness = thickness
                hasArrow  = AVal.constant false
                text      = AVal.constant (Some "Z")
                fix       = AVal.constant false
            }

            //test
            //let n90 =
            //    adaptive {
            //        let! up = model.up.value
            //        let! n = model.north.value
            //        let! o = model.origin
            //        return updateVectorInDegree up n o 90.0
            //    }
                     
            //let styleNorth90 : MarkerStyle = {
            //    position  = model.origin
            //    direction =  n90 //AVal.constant(V3d(19417.0692,323595.2722,-323129.0951))//
            //    color     = AVal.constant C4b.Yellow
            //    size      = size
            //    thickness = thickness
            //    hasArrow  = AVal.constant true
            //    text      = AVal.constant (Some "90°")
            //    fix       = AVal.constant false
            //}

            let refSysTrafo2 =
              adaptive {
                  let! north = model.northO
                  let! up = model.up.value
                  let east = north.Normalized.Cross(up.Normalized)
                  
                  return Trafo3d.FromOrthoNormalBasis(north.Normalized,east,up.Normalized)
              }

            
            let sizeText = 
                Sg.text 
                    cam 
                    near 
                    (AVal.constant 60.0) 
                    model.origin 
                    (model.origin |> AVal.map Trafo3d.Translation) 
                    (AVal.constant 0.05)
                    model.selectedScale 

            let refsystem = 
              Sg.ofList [
                  point model.origin (AVal.constant C4b.Red) cam
                  sizeText
                  styleUp    |> directionMarker near cam  
                  styleNorth |> directionMarker near cam
                  //styleNorth90 |> directionMarker near cam
                  styleEast  |> directionMarker near cam
              ]
            
            let translation = styleX.position |> AVal.map Trafo3d.Translation
            let inv (t:aval<Trafo3d>) = t |> AVal.map(fun x -> x.Inverse)

            let xyzSystem = 
              Sg.ofList [                
                styleX  |> directionMarker near cam  
                styleY  |> directionMarker near cam  
                styleZ  |> directionMarker near cam
              ] 
                //|> Sg.trafo (translation |> inv)
                //|> Sg.trafo refSysTrafo2   
                //|> Sg.trafo translation

            [refsystem] |> Sg.ofList |> Sg.onOff(model.isVisible)  
                

    module UI =

        let view (model:AdaptiveReferenceSystem) (camera : AdaptiveCameraControllerState) =
            let bearing = 
                adaptive {
                    let! up = model.up.value
                    let! north = model.northO //model.north.value 
                    let! view = camera.view
                    return DipAndStrike.bearing up north view.Forward
                    }

            let pitch = 
                adaptive {
                    let! up = model.up.value
                    let! view = camera.view
                    return DipAndStrike.pitch up view.Forward
                    }

            let altitude = 
                adaptive {
                        let! pos = model.origin
                        let! planet = model.planet
                        let! up = model.up.value
                        return CooTransformation.getAltitude pos up planet
                    }

            require GuiEx.semui (
                Html.table [                                                
                    Html.row "Pos:"     [Incremental.text (model.origin     |> AVal.map (fun x -> x.ToString("0.00")))]
                    Html.row "Up:"      [Vector3d.view model.up |> UI.map SetUp ]
                    Html.row "North:"   [Incremental.text (model.north.value |> AVal.map (fun x -> x.ToString("0.00")))]
                    Html.row "NorthO:"  [Incremental.text (model.northO |> AVal.map (fun x -> x.ToString("0.00")))]
                    Html.row "N-Offset:"[Numeric.view' [InputBox] model.noffset |> UI.map SetNOffset ] 
                    Html.row "Bearing:" [Incremental.text (bearing |> AVal.map (fun x -> x.ToString("0.00")))] // compute azimuth with view dir, north vector and up vector
                    Html.row "Pitch:"   [Incremental.text (pitch |> AVal.map (fun x -> x.ToString("0.00")))]  // same for pitch which relates to dip angle
                    Html.row "Altitude:"  [Incremental.text (altitude |> AVal.map (fun x -> x.ToString("0.00")))]
                    Html.row "Visible:" [GuiEx.iconCheckBox model.isVisible ToggleVisible ]
                      
                ]
            )
