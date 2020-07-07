namespace PRo3D

open System
open System.IO

open Aardvark.Base
open Aardvark.Base.Geometry
open Aardvark.Base.Incremental
open Aardvark.Base.Incremental.Operators
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.UI.Animation
open Aardvark.Rendering.Text

open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives.Sg
open Aardvark.GeoSpatial.Opc.Shader
open Aardvark.GeoSpatial.Opc
open FShade

open PRo3D.ReferenceSystem
open PRo3D.Surfaces
open PRo3D.Viewer
open PRo3D.Groups
open PRo3D.Viewplanner
open PRo3D.Viewplanner.Mutable


module ViewerUtils =    
    open Aardvark.GeoSpatial.Opc.Shader
    open FShade
    open PRo3D

    //let _surfaceModelLens = Model.Lens.scene |. Scene.Lens.surfacesModel
    //let _flatSurfaces = Scene.Lens.surfacesModel |. SurfaceModel.Lens.surfaces |. GroupsModel.Lens.flat

    let create2DHaltonRandomSeries =
        new HaltonRandomSeries(2, new RandomSystem(System.DateTime.Now.Second))

    let create1DHaltonRandomSeries =
        new HaltonRandomSeries(1, new RandomSystem(System.DateTime.Now.Second))

    let genRandomNumbers count =
        let rnd = System.Random()
        List.init count (fun _ -> rnd.Next())

    let genRandomNumbersBetween count min max =
        let rnd = System.Random()
        List.init count (fun _ -> rnd.Next(min, max))
        

    let computeSCRayRaster (number : int) (view : CameraView) (frustum : Frustum) (haltonRandom : HaltonRandomSeries) =
        [
        for i in [| 0 .. number-1|] do
            let x = frustum.left   + (haltonRandom.UniformDouble(0) * (frustum.right - frustum.left));
            let y = frustum.bottom + (haltonRandom.UniformDouble(1) * (frustum.top - frustum.bottom));
            
            let centralPointonNearPlane = view.Location + (view.Forward * frustum.near)
            let newPointOnNearPlane = centralPointonNearPlane + (view.Right * x) + (view.Up * y)
            let transformedForwardRay = new Ray3d(view.Location, (newPointOnNearPlane - view.Location).Normalized)

            yield transformedForwardRay            
        ]  

    let mutable lastHash = -1  

    let getSinglePointOnSurface (m : Model) (ray : Ray3d) (cameraLocation : V3d ) = 
        let mutable cache = hmap.Empty
        let rayHash = ray.GetHashCode()

        if rayHash = lastHash then
            None
        else    
            let onlyActive (id : Guid) (l : Leaf) (s : SgSurface) = l.active
            let onlyVisible (id : Guid) (l : Leaf) (s : SgSurface) = l.visible

            let surfaceFilter = 
               match m.interaction with
               | Interactions.PickSurface -> onlyVisible
               | _ -> onlyActive

            Log.startTimed "[RayCastSurface] try intersect kdtree"                                                             
            let hitF (camLocation : V3d) (p : V3d) = 
                let ray =
                    let dir = (p-camLocation).Normalized
                    FastRay3d(camLocation, dir) 
                //let doKdTreeIntersection (m : SurfaceModel) (refSys : PRo3D.ReferenceSystem.ReferenceSystem) (r : FastRay3d) (cache : option<float * PRo3D.Surfaces.Surface> * hmap<_,_>)  = 
                match SurfaceApp.doKdTreeIntersection m.scene.surfacesModel m.scene.referenceSystem ray surfaceFilter cache with
                    | Some (t,surf), c ->                             
                        cache <- c; ray.Ray.GetPointOnRay t |> Some
                    | None, c ->
                        cache <- c; None
                                  
            let result = 
                match SurfaceApp.doKdTreeIntersection m.scene.surfacesModel m.scene.referenceSystem (FastRay3d(ray)) surfaceFilter cache with
                | Some (t,surf), c ->                         
                    cache <- c
                    let hit = ray.GetPointOnRay(t)
                   
                    lastHash <- rayHash
                    match hitF cameraLocation hit with
                    | None -> None
                    | Some projectedPoint -> Some projectedPoint
                | None, _ -> 
                    Log.error "[RayCastSurface] no hit"
                    None
            Log.stop()
            Log.line "done intersecting"
                
            result 

    let getPointsOnSurfaces (m : Model) (rays : list<Ray3d>) (camLocation : V3d ) = 
        rays |> List.choose( fun ray -> getSinglePointOnSurface m ray camLocation)

    //let getHaltonRandomTrafos (count : int) (m : Model) =
    let getHaltonRandomTrafos (shattercone : SnapshotShattercone) (m : Model) =
        let haltonSeries = create2DHaltonRandomSeries
        let rays = computeSCRayRaster shattercone.count m.scene.cameraView m.frustum haltonSeries
        let points = getPointsOnSurfaces m rays m.scene.cameraView.Location 

        let hsScaling = 
            match shattercone.scale with
            | Some s -> let rs = genRandomNumbersBetween shattercone.count s.X s.Y
                        rs |> List.map(fun x -> (float)x/100.0) 
            | None -> [ for i in 1 .. shattercone.count -> 1.0 ]

        let xRotation =
            match shattercone.xRotation with
            | Some rx -> genRandomNumbersBetween shattercone.count rx.X rx.Y
            | None -> [ for i in 1 .. shattercone.count -> 0 ]
        
        //let yRotation = genRandomNumbersBetween shattercone.count 45 135
        let yRotation = 
            match shattercone.yRotation with
            | Some ry -> genRandomNumbersBetween shattercone.count ry.X ry.Y
            | None -> [ for i in 1 .. shattercone.count -> 0 ]

        let zRotation =
            match shattercone.zRotation with
            | Some rz -> genRandomNumbersBetween shattercone.count rz.X rz.Y //0 360
            | None -> [ for i in 1 .. shattercone.count -> 0 ]
        //let zRotation = genRandomNumbersBetween shattercone.count 0 360

        let trafos =
            [
            for i in 0..points.Length-1 do
                yield Trafo3d.Scale(float hsScaling.[i]) * 
                Trafo3d.RotationZInDegrees(float zRotation.[i]) *
                Trafo3d.RotationYInDegrees(float yRotation.[i]) *
                Trafo3d.RotationXInDegrees(float xRotation.[i]) *
                Trafo3d.Translation(points.[i])
            ]
            

        points, trafos //points |> List.map( fun p -> Trafo3d.Scale(0.03) * Trafo3d.Translation(p) )
        

    let viewHaltonSeries (points : IMod<list<V3d>>) =
        let points =
            aset{
                let! points = points
                let pnts = 
                    points 
                    |> List.map( fun p -> PRo3D.Sg.dot (Mod.constant p) (Mod.constant 5.0) (Mod.constant C4b.Cyan) )
                    |> Sg.ofList
                yield pnts
                } |> Sg.set
        points
        
            
    //let getSgSurfacesWithBBIntersection (surfaces : plist<Surface>) (trafos : list<Trafo3d>) (sgs : SgSurface) = 
    let getSgSurfacesWithBBIntersection (newSurfaces : plist<Surface>) (sgSurfaces : list<Guid*SgSurface>) (newSg : SgSurface) = 
        let mutable sgsurfs = sgSurfaces //[]
        let mutable sgsurfsout = []
        let mutable testSfs = newSurfaces
        let sgSurfs =
            for i in [|0..newSurfaces.Count-1|] do
                let newSgSurf = {newSg with surface = newSurfaces.[i].guid}

                // put the first sgsurface in the list
                if sgsurfs.IsEmpty then 
                    sgsurfs <- sgsurfs @ [(newSgSurf.surface, newSgSurf)]

                // check for the new Sgsurface if bb intersects with others
                else
                    let obj1 = newSgSurf.globalBB.Transformed(newSurfaces.[i].preTransform) 
                    let addSurf = 
                        [
                        for x in 0..sgsurfs.Length-1 do
                            let surf2 = newSurfaces |> PList.toList |> List.find(fun s -> (fst sgsurfs.[x]) = s.guid)
                            let obj2 = (snd sgsurfs.[x]).globalBB.Transformed(surf2.preTransform)
                            yield (obj1).Intersects(obj2)
                            ]
                    if addSurf |> List.contains true then
                        // TEST: this surface bb intersects with another one and would be discarded 
                        sgsurfsout <- sgsurfsout @ [(newSgSurf.surface, newSgSurf)]
                        let sfs = {newSurfaces.[i] with colorCorrection = 
                                        {newSurfaces.[i].colorCorrection with color = {c = C4b.Red}; useColor = true}}
                        let testSurfs = testSfs |> PList.map(fun x -> if x.guid = newSgSurf.surface then sfs else x)
                        testSfs <- testSurfs
                    else
                        sgsurfs <- sgsurfs @ [(newSgSurf.surface, newSgSurf)]
               
                
        // keep only the remaining surfaces
        let sfs = sgsurfs |> List.map(fun sg -> newSurfaces |> PList.toList |> List.find(fun s -> (fst sg) = s.guid))
        sfs, sgsurfs

        //TEST: add the discarded
        //testSfs |> PList.toList, sgsurfs @ sgsurfsout

    
                          
    let colormap = 
        let config = { wantMipMaps = false; wantSrgb = false; wantCompressed = false }
        FileTexture("resources/HueColorMap.png",config) :> ITexture

    let pickable' (pick :IMod<Pickable>) (sg: ISg) =
        Sg.PickableApplicator (pick, Mod.constant sg)
    
    let toModSurface (leaf : IMod<MLeaf>) = 
         adaptive {
            let! c = leaf
            match c with 
                | MSurfaces s -> return s
                | _ -> return c |> sprintf "wrong type %A; expected MSurfaces" |> failwith
            }

    let lookUp guid (table:amap<Guid, IMod<MLeaf>>) =
        
        let entry = table |> AMap.find guid

        entry |> Mod.bind(fun x -> x |> toModSurface)
    
    let addImageCorrectionParameters (surf:IMod<MSurface>)  (isg:ISg<'a>) =
        
            //Mod.bind(fun x -> lookUp (x.surface) blarg )
        let contr    = surf |> Mod.bind( fun x -> x.colorCorrection.contrast.value )
        let useContr = surf |> Mod.bind( fun x -> x.colorCorrection.useContrast )
        let bright   = surf |> Mod.bind( fun x -> x.colorCorrection.brightness.value )
        let useB     = surf |> Mod.bind( fun x -> x.colorCorrection.useBrightn) 
        let gamma    = surf |> Mod.bind( fun x -> x.colorCorrection.gamma.value )
        let useG     = surf |> Mod.bind( fun x -> x.colorCorrection.useGamma )
        let useGray  = surf |> Mod.bind( fun x -> x.colorCorrection.useGrayscale )
        let useColor = surf |> Mod.bind( fun x -> x.colorCorrection.useColor )
        let color    = surf |> Mod.bind( fun x -> x.colorCorrection.color.c )

        isg
            |> Sg.uniform "useContrastS"   useContr  //image correction
            |> Sg.uniform "contrastS"      contr
            |> Sg.uniform "useBrightnS"    useB
            |> Sg.uniform "brightnessS"    bright
            |> Sg.uniform "useGammaS"      useG 
            |> Sg.uniform "gammaS"         gamma
            |> Sg.uniform "useGrayS"       useGray
            |> Sg.uniform "useColorS"      useColor
            |> Sg.uniform "colorS"         color


   // let viewSingleSurfaceSg (surface : MSgSurface) (surfaceTable : amap<Guid, IMod<MLeaf>>) (frustum : IMod<Frustum>) (selectedId : IMod<Option<Guid>>) (isctrl:IMod<bool>) (globalBB : IMod<Box3d>) (refsys:MReferenceSystem) =
    

    let addAttributeFalsecolorMappingParameters (surf:IMod<MSurface>)  (isg:ISg<'a>) =
            
        let selectedScalar =
            adaptive {
                let! s = surf
                let! scalar = s.selectedScalar
                return scalar.IsSome
            }  
      
        let scalar = surf |> Mod.bind( fun x -> x.selectedScalar )

        //let texturLayer =  
        //    adaptive {
        //        let! scalar = scalar
        //        let! s = surf
        //        match scalar with
        //         | Some sc -> 
        //            return s.textureLayers |> AList.toList
        //                                   |> List.find( fun (tl:TextureLayer) -> tl.label = (Mod.force sc.label ))
        //         | None -> return list.Empty
               
        //    }  
      

        let interval = scalar |> Mod.bind ( fun x ->
                        match x with 
                            | Some s -> s.colorLegend.interval.value
                            | None   -> Mod.constant(1.0)
                        )

        let inverted = scalar |> Mod.bind ( fun x ->
                        match x with 
                            | Some s -> s.colorLegend.invertMapping
                            | None   -> Mod.constant(false)
                        )     
        
        let upperB = scalar |> Mod.bind ( fun x ->
                        match x with 
                            | Some s -> s.colorLegend.upperBound.value
                            | None   -> Mod.constant(1.0)
                        )

        let lowerB = scalar |> Mod.bind ( fun x ->
                        match x with 
                            | Some s -> s.colorLegend.lowerBound.value
                            | None   -> Mod.constant(1.0)
                        )

        let upperC = 
          scalar 
            |> Mod.bind (fun x ->
               match x with 
                 | Some s -> 
                   s.colorLegend.upperColor.c 
                     |> Mod.map(fun x -> 
                       let t = x.ToC3f()
                       let t1 = HSVf.FromC3f(t)
                       let t2 = (float)t1.H
                       t2)
                 | None -> Mod.constant(1.0)
               )
        let lowerC = 
          scalar 
            |> Mod.bind ( fun x ->
              match x with 
                | Some s -> 
                  s.colorLegend.lowerColor.c 
                    |> Mod.map(fun x -> ((float)(HSVf.FromC3f (x.ToC3f())).H))
                | None   -> Mod.constant(0.0)
              )

        let rangeToMinMax = 
          scalar 
            |> Mod.bind (fun x ->
              match x with 
                  | Some s -> s.definedRange |> Mod.map(fun y -> V2d(y.Min, y.Max))
                  | None   -> Mod.constant(V2d(0.0,1.0))
              )
        isg
            |> Sg.uniform "falseColors"    selectedScalar
            |> Sg.uniform "startC"         lowerC  
            |> Sg.uniform "endC"           upperC
            |> Sg.uniform "interval"       interval
            |> Sg.uniform "inverted"       inverted
            |> Sg.uniform "lowerBound"     lowerB
            |> Sg.uniform "upperBound"     upperB
            |> Sg.uniform "MinMax"         rangeToMinMax
            |> Sg.texture (Sym.ofString "ColorMapTexture") (Mod.constant colormap)

    let getLodParameters (surf:IMod<MSurface>) (refsys:MReferenceSystem) (frustum : IMod<Frustum>) =
        adaptive {
            let! s = surf
            let! frustum = frustum 
            let sizes = V2i(1024,768)
            let! quality = s.quality.value
            //let! trafo = Mod.map2(fun a b -> a * b) s.preTransform s.transformation.trafo //combine pre and current transform
            let! trafo = PRo3D.Transformations.fullTrafo surf refsys
            
            return { frustum = frustum; size = sizes; factor = Math.Pow(Math.E, quality); trafo = trafo }
        }
    let getLodParameters' (surf:Surface) (frustum : Frustum) =
        let sizes = V2i(1024,768)
        let quality = surf.quality.value
        let trafo = surf.preTransform //combine pre and current transform
            
        { frustum = frustum; size = sizes; factor = Math.Pow(Math.E, quality); trafo = trafo }
        
    
    let attributeParameters (surf:IMod<MSurface>) =
         adaptive {
            let! s = surf
            let! scalar = s.selectedScalar
            let! scalar' = 
                match scalar with
                | Some m -> m.label |> Mod.map Some
                | None -> Mod.constant None //scalar |> Option.map(fun x -> x.index) //option<IMod<int>>
            
            let! texture = s.selectedTexture 
            let attr : AttributeParameters = 
                {
                    selectedTexture = texture |> Option.map(fun x -> x.index)
                    selectedScalar  = scalar'//scalar  |> Option.map(fun x -> x.index |> Mod.force)
                }

            return attr
        }

    let attributeParameters' (surf:Surface) =
            let s = surf
            let scalar = s.selectedScalar
            let scalar' = 
                match scalar with
                | Some m -> m.label |> Some
                | None -> None //scalar |> Option.map(fun x -> x.index) //option<IMod<int>>
            
            let texture = s.selectedTexture 
            let attr : AttributeParameters = 
                {
                    selectedTexture = texture |> Option.map(fun x -> x.index)
                    selectedScalar  = scalar'//scalar  |> Option.map(fun x -> x.index |> Mod.force)
                }

            attr
        

    let viewSingleSurfaceSg 
        (surface : MSgSurface) 
        (blarg : amap<Guid, IMod<MLeaf>>) 
        (frustum : IMod<Frustum>) 
        (selectedId : IMod<Option<Guid>>)
        (isctrl:IMod<bool>) 
        (globalBB : IMod<Box3d>) 
        (refsys:MReferenceSystem) 
        (fp:MFootPrint) 
        (vp:IMod<Option<MViewPlan>>) 
        (useHighlighting:IMod<bool>) 
        (filterTexture : IMod<bool>) =

        adaptive {
          let! exists = (blarg |> AMap.keys) |> ASet.contains surface.surface
          if exists then
            
            let surf = lookUp (surface.surface) blarg
                //Mod.bind(fun x -> lookUp (x.surface) blarg )
            
            let isSelected = Mod.map2(fun x y ->
              match x with
                | Some id -> (id = surface.surface) && y
                | None -> false) selectedId useHighlighting
            
            let createSg (sg : ISg) =
                sg 
                |> Sg.noEvents 
                |> Sg.cullMode(surf |> Mod.bind(fun x -> x.cullMode))
                |> Sg.fillMode(surf |> Mod.bind(fun x -> x.fillMode))
            
            let pickable = 
              Mod.map2( fun (a:Box3d) (b:Trafo3d) -> 
                { shape = PickShape.Box (a.Transformed(b)); trafo = Trafo3d.Identity }) globalBB (Transformations.fullTrafo surf refsys)
            
            let pickBox = 
              pickable 
                |> Mod.map(fun k ->
                  match k.shape with
                    | PickShape.Box bb -> bb
                    | _ -> Box3d.Invalid)
            
            let triangleFilter = surf |> Mod.bind(fun s -> s.triangleSize.value)
            
            let trafo =
               adaptive {
                    let! fullTrafo = Transformations.fullTrafo surf refsys
                    let! s = surf
                    let! sc = s.scaling.value
                    let! t = s.preTransform
                    return Trafo3d.Scale(sc) * (t * fullTrafo )
                }
                
            let trafoObj =
               adaptive {
                    let! s = surf
                    let! t = s.preTransform
                    return t
                }

            let! filterTexture = filterTexture
            
            let magnificationFilter = 
                match filterTexture with
                | false -> TextureFilterMode.Point
                | true -> TextureFilterMode.Linear // HERA/MARS-DL, default for snapshots
            
            let samplerDescription : SamplerStateDescription -> SamplerStateDescription = 
                (fun x -> 
                    x.Filter <- new TextureFilter(TextureFilterMode.Linear, magnificationFilter, TextureFilterMode.Linear, true ); 
                    x)           
            let footprintVisible = //Mod.map2 (fun (vp:Option<MViewPlan>) vis -> (vp.IsSome && vis)) vp, fp.isVisible
                adaptive {
                    let! vp = vp
                    let! visible = fp.isVisible
                    let! id = fp.vpId
                    return (vp.IsSome && visible)
                }
            
            let footprintMatrix = 
                adaptive {
                    let! fppm = fp.projectionMatrix
                    let! fpvm = fp.instViewMatrix
                    let! s = surf
                    let! ts = s.preTransform
                    let! t = trafo
                    //return (t.Forward * fpvm * fppm) //* t.Forward 
                    return (fppm * fpvm) // * ts.Forward
                } 
            let structuralOnOff (visible : IMod<bool>) (sg : ISg<_>) : ISg<_> = 
                visible 
                |> Mod.map (fun visible -> 
                    if visible then sg else Sg.empty
                )
                |> Sg.dynamic
            let test =             
              surface.sceneGraph
                |> Mod.map createSg
                |> Sg.dynamic
                |> Sg.trafo trafo //(Transformations.fullTrafo surf refsys)
                |> Sg.modifySamplerState (DefaultSemantic.DiffuseColorTexture)(Mod.constant(samplerDescription))
                |> Sg.uniform "selected"      (isSelected) // isSelected
                |> Sg.uniform "selectionColor" (Mod.constant (C4b (200uy,200uy,255uy,255uy)))
                |> addAttributeFalsecolorMappingParameters surf
                |> Sg.uniform "TriangleSize"   triangleFilter  //triangle filter
                |> addImageCorrectionParameters surf
                |> Sg.uniform "footprintVisible" footprintVisible
                |> Sg.uniform "instrumentMVP" footprintMatrix
                |> Sg.uniform "projMVP" fp.projectionMatrix
                |> Sg.uniform "globalToLocal" fp.globalToLocalPos
                |> Sg.uniform "instViewMVP" fp.instViewMatrix
                |> Sg.texture (Sym.ofString "FootPrintTexture") fp.projTex
                |> Sg.LodParameters( getLodParameters surf refsys frustum )
                |> Sg.AttributeParameters( attributeParameters surf )
                |> pickable' pickable
                |> Sg.noEvents 
                |> Sg.withEvents [
                     SceneEventKind.Click, (
                       fun sceneHit -> 
                         let surfM = surf       |> Mod.force
                         let name  = surfM.name |> Mod.force                                                  
                         Log.warn "spawning picksurface %s" name
                         true, Seq.ofList [PickSurface (sceneHit,name)])                         
                   ]  
                // handle surface visibility
                //|> Sg.onOff (surf |> Mod.bind(fun x -> x.isVisible)) // on off variant
                |> structuralOnOff  (surf |> Mod.bind(fun x -> x.isVisible)) // structural variant
              |> Sg.andAlso (
                (Sg.wireBox (C4b.VRVisGreen |> Mod.constant) pickBox) 
                  |> Sg.noEvents
                  |> Sg.effect [              
                      Shader.stableTrafo |> toEffect 
                      DefaultSurfaces.vertexColor |> toEffect
                    ] |> Sg.onOff ~~false
                )
            
            return test
          else
            return Sg.empty
        } |> Sg.dynamic
        
    let frustum (m:MModel) =
        let near = m.scene.config.nearPlane.value
        let far = m.scene.config.farPlane.value
        (Navigation.UI.frustum near far)

    let fixAlpha (v : Vertex) =
        fragment {         
           return V4d(v.c.X, v.c.Y,v.c.Z, 1.0)           
        }

    let triangleFilterX (input : Triangle<Vertex>) =
        triangle {
            let p0 = input.P0.pos.XYZ
            let p1 = input.P1.pos.XYZ
            let p2 = input.P2.pos.XYZ

            let maxSize = uniform?TriangleSize

            let a = (p1 - p0)
            let b = (p2 - p1)
            let c = (p0 - p2)

            let alpha = a.Length < maxSize
            let beta  = b.Length < maxSize
            let gamma = c.Length < maxSize

            let check = (alpha && beta && gamma)
            if check then
                yield input.P0 
                yield input.P1
                yield input.P2
        }

    let surfaceEffect =
        Effect.compose [
            Shader.stableTrafo             |> toEffect
            triangleFilterX                |> toEffect
            PRo3D.Base.OtherShader.Shader.improvedDiffuseTexture |> toEffect
            fixAlpha |> toEffect
            
            // selection coloring makes gamma correction pointless. remove if we are happy withmark PatchBorders
            // Shader.selectionColor          |> toEffect       
            PRo3D.Base.OtherShader.Shader.markPatchBorders |> toEffect
            
            
            Shader.LoDColor                |> toEffect                             
            PRo3D.Base.OtherShader.Shader.falseColorLegend2 |> toEffect
            PRo3D.Base.OtherShader.Shader.mapColorAdaption  |> toEffect            
            //PRo3D.Base.OtherShader.Shader.footprintV        |> toEffect //TODO reactivate viewplanner
            //PRo3D.Base.OtherShader.Shader.footPrintF        |> toEffect
        ]

    let getSurfacesScenegraphs (m:MModel) =
        let sgGrouped = m.scene.surfacesModel.sgGrouped

        

      //  let renderCommands (sgGrouped:alist<amap<Guid,MSgSurface>>) overlayed depthTested (m:MModel) =
        let usehighlighting = true |> Mod.constant //m.scene.config.useSurfaceHighlighting
        let selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
        let refSystem = m.scene.referenceSystem
        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.map(fun _ sf -> 
                        let bla = m.scene.surfacesModel.surfaces.flat
                        viewSingleSurfaceSg sf bla m.frustum selected m.ctrlFlag sf.globalBB refSystem m.footPrint m.scene.viewPlans.selectedViewPlan usehighlighting m.filterTexture)
                    |> AMap.toASet 
                    |> ASet.map snd                     
                )                
            )
        //grouped   
        let sgs =
            alist {        
                let mutable i = 0
                for set in grouped do
                    i <- i + 1
                    let sg = 
                        set 
                        |> Sg.set
                        |> Sg.effect [surfaceEffect]
                        |> Sg.uniform "LoDColor" (Mod.constant C4b.Gray)
                        |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring //()                        

                    yield  sg

                        //if i = c then //now gets rendered multiple times
                         // assign priorities globally, or for each anno and make sets
            
            }                              
        sgs
  
    let getSurfacesSgWithCamera (m : MModel) =
        let sgs = getSurfacesScenegraphs m
        let camera =
            Mod.map2 (fun v f -> Camera.create v f) m.scene.cameraView m.frustum 
        sgs 
            |> ASet.ofAList
            |> Sg.set
            |> (camera |> Sg.camera)

    let renderCommands (sgGrouped:alist<amap<Guid,MSgSurface>>) overlayed depthTested (m:MModel) =
        let usehighlighting = true |> Mod.constant //m.scene.config.useSurfaceHighlighting
        let filterTexture = ~~true

        let selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
        let refSystem = m.scene.referenceSystem
        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.map(fun _ sf -> 
                        let bla = m.scene.surfacesModel.surfaces.flat
                        viewSingleSurfaceSg sf bla m.frustum selected m.ctrlFlag sf.globalBB refSystem m.footPrint m.scene.viewPlans.selectedViewPlan usehighlighting filterTexture)
                    |> AMap.toASet 
                    |> ASet.map snd                     
                )                
            )
        //grouped   
        alist {        
            let mutable i = 0
            for set in grouped do
                i <- i + 1
                let sg = 
                    set 
                    |> Sg.set
                    |> Sg.effect [surfaceEffect]
                    |> Sg.uniform "LoDColor" (Mod.constant C4b.Gray)
                    |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring //()                        

                yield RenderCommand.SceneGraph sg

                //if i = c then //now gets rendered multiple times
                 // assign priorities globally, or for each anno and make sets
                yield RenderCommand.SceneGraph depthTested

                yield Aardvark.UI.RenderCommand.Clear(None,Some (Mod.constant 1.0))
            
            yield RenderCommand.SceneGraph overlayed
            
        }  

    let renderScreenshot (runtime : IRuntime) (size : V2i) (sg : ISg<ViewerAction>) = 
        let col = runtime.CreateTexture(size, TextureFormat.Rgba8, 1, 1);
        let signature = 
            runtime.CreateFramebufferSignature [
                DefaultSemantic.Colors, { format = RenderbufferFormat.Rgba8; samples = 1 }
            ]

        let fbo = 
            runtime.CreateFramebuffer(
                signature, 
                Map.ofList [
                    DefaultSemantic.Colors, col.GetOutputView()
                ]
            )

        let taskclear = runtime.CompileClear(signature,Mod.constant C4f.Black,Mod.constant 1.0)
        
        let task = runtime.CompileRender(signature, sg)

        taskclear.Run(null, fbo |> OutputDescription.ofFramebuffer) |> ignore
        task.Run(null, fbo |> OutputDescription.ofFramebuffer) |> ignore
        let colorImage = runtime.Download(col)
        colorImage
module GaleCrater =
    open PRo3D.Base
    
    let galeBounds = Box2i(V2i(3,9), V2i(19,16))
    let isGale x = x.importPath |> String.contains "MslGaleDem"
    let galeTrafo = V3d(0.0,0.0,-560.92)

    let _translation = Surface.Lens.transformation |. Transformations.Lens.translation |. V3dInput.Lens.value

    let hack surfaces =

        let surfaces =
            surfaces                         
            |> PList.filter(fun x -> 
                if isGale x then
                    let parsedPath = 
                        x.importPath 
                        |> Path.GetFileName 
                        |> String.split('_')
                    
                    let gridCoord = new V2i((parsedPath.[1] |> Int32.Parse), (parsedPath.[2] |> Int32.Parse))
                    galeBounds.Contains(gridCoord)                          
                else
                    true
            )
            |> PList.map(fun x ->
                if isGale x then
                    x |> Lenses.set _translation galeTrafo
                else    
                    x
            )
        surfaces
