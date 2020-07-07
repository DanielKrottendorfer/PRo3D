﻿namespace PRo3D

open Aardvark.Service

open System
open System.Collections.Concurrent
open System.IO
open System.Diagnostics

open Aardvark.Base
open Aardvark.Base.Geometry
open Aardvark.Base.Incremental
open Aardvark.Base.Incremental.Operators
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.Rendering.Text
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.UI.Animation
open Aardvark.Application

open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives.Sg
open Aardvark.VRVis

open MBrace.FsPickler
open System.IO

open PRo3D
open PRo3D.Base
open PRo3D.Navigation2
open PRo3D.Bookmarkings
open PRo3D.ReferenceSystem
open PRo3D.Surfaces
open PRo3D.Viewer
open PRo3D.Drawing
open PRo3D.Groups
open PRo3D.OrientationCube
open PRo3D.Linking
open PRo3D.Base.Annotation

open PRo3D.Viewplanner
open PRo3D.Minerva
open PRo3D.Linking
open PRo3D.Correlations
open CorrelationDrawing.Model
open Scene   

open Aardium

open MBrace.FsPickler
open Chiron
open CorrelationDrawing.LogTypes
open CorrelationDrawing.Nuevo
open CorrelationDrawing.AnnotationTypes
 
type UserFeedback<'a> = {
    id      : string
    text    : string
    timeout : int
    msg     : 'a
}

module UserFeedback =

    let create duration text =
        {
            id      = System.Guid.NewGuid().ToString()
            text    = text
            timeout = duration
            msg     = ViewerAction.NoAction ""
        }

    let createWorker (feedback: UserFeedback<'a>) =
        proclist {
            yield UpdateUserFeedback ""
            yield UpdateUserFeedback feedback.text
            yield feedback.msg
      
            do! Proc.Sleep feedback.timeout
            yield ThreadsDone feedback.id
        }

    let queueFeedback fb m =
        { m with scene = { m.scene with feedbackThreads = ThreadPool.add fb.id (fb |> createWorker) m.scene.feedbackThreads }}

module ViewerApp =         
                     
    // surfaces
    let _surfacesModel   = Model.Lens.scene |. Scene.Lens.surfacesModel        
    let _sgSurfaces      = _surfacesModel   |. SurfaceModel.Lens.sgSurfaces
    let _selectedSurface = _surfacesModel   |. SurfaceModel.Lens.surfaces  |. GroupsModel.Lens.singleSelectLeaf
       
    // navigation
    let _navigation = Model.Lens.navigation
    let _camera     = _navigation |. NavigationModel.Lens.camera
    let _view       = _camera |. CameraControllerState.Lens.view

    // drawing
    let _drawing         = Model.Lens.drawing
    let _annotations     = _drawing |. DrawingModel.Lens.annotations
    let _dnsColorLegend  = _drawing |. DrawingModel.Lens.dnsColorLegend
    let _flat            = _annotations  |. GroupsModel.Lens.flat    
    let _lookUp          = _annotations  |. GroupsModel.Lens.groupsLookup
    let _groups          = _annotations  |. GroupsModel.Lens.rootGroup |. Node.Lens.subNodes

    // animation  
    let _animation = Model.Lens.animations
    let _animationView = _animation |. AnimationModel.Lens.cam

    //footprint
    let _footprint = Model.Lens.footPrint
       
    let lookAtData (m: Model) =         
        let bb = m |> Lenses.get _sgSurfaces |> HMap.toSeq |> Seq.map(fun (_,x) -> x.globalBB) |> Box3d.ofSeq
        let view = CameraView.lookAt bb.Max bb.Center m.scene.referenceSystem.up.value             

        _view.Set(m,view)

    let lookAtBoundingBox (bb: Box3d) (m: Model) =
        let view = CameraView.lookAt bb.Max bb.Center m.scene.referenceSystem.up.value                
        m |> Lenses.set _view view
    
    let lookAtSurface (m: Model) id =
        let surf = m |> Lenses.get _sgSurfaces |> HMap.tryFind id
        match surf with
            | Some s ->
                let bb = s.globalBB
                m |> lookAtBoundingBox s.globalBB
            | None -> m

    let logScreen timeout m text = 
      let feedback = 
        {
          id      = System.Guid.NewGuid().ToString()
          text    = text
          timeout = timeout
          msg     = ViewerAction.NoAction ""
        }
      m |> UserFeedback.queueFeedback feedback

    let stash (model : Model) =
        { model with past = Some model.drawing; future = None }
       
    let refConfig : ReferenceSystemConfig<ViewConfigModel> =
        { 
            arrowLength    = ViewConfigModel.Lens.arrowLength    |. NumericInput.Lens.value
            arrowThickness = ViewConfigModel.Lens.arrowThickness |. NumericInput.Lens.value
            nearPlane      = ViewConfigModel.Lens.nearPlane      |. NumericInput.Lens.value
        }

    let mrefConfig : ReferenceSystemApp.MInnerConfig<MViewConfigModel> =
        {
            getArrowLength    = fun (x:MViewConfigModel) -> x.arrowLength.value
            getArrowThickness = fun (x:MViewConfigModel) -> x.arrowThickness.value
            getNearDistance   = fun (x:MViewConfigModel) -> x.nearPlane.value
        }
    
    let drawingConfig : DrawingApp.SmallConfig<ReferenceSystem> =
        { 
            up     = (ReferenceSystem.Lens.up     |. V3dInput.Lens.value)
            north  = (ReferenceSystem.Lens.northO) //  |. V3dInput.Lens.value)
            planet = (ReferenceSystem.Lens.planet)
        }

    let mdrawingConfig : DrawingApp.MSmallConfig<MViewConfigModel> =
        {            
            getNearPlane       = fun x -> x.nearPlane.value
            getHfov            = fun (x:MViewConfigModel) -> ((Mod.init 60.0) :> IMod<float>)
            getArrowThickness  = fun (x:MViewConfigModel) -> x.arrowThickness.value
            getArrowLength     = fun (x:MViewConfigModel) -> x.arrowLength.value
            getDnsPlaneSize    = fun (x:MViewConfigModel) -> x.dnsPlaneSize.value
            getOffset          = fun (x:MViewConfigModel) -> Mod.constant(0.1)//x.offset.value
        }

    let navConf : Navigation.smallConfig<ViewConfigModel, ReferenceSystem> =
        {
            navigationSensitivity = ViewConfigModel.Lens.navigationSensitivity |. NumericInput.Lens.value
            up                    = ReferenceSystem.Lens.up |. V3dInput.Lens.value
        }

    let updateCameraUp (m: Model) =
        let cam = m.navigation.camera
        let view' = CameraView.lookAt cam.view.Location (cam.view.Location + cam.view.Forward) m.scene.referenceSystem.up.value
        let cam' = { cam with view = view' }
        _camera.Set(m,cam')    
    
    let mutable cache = hmap.Empty

    let updateSceneWithNewSurface (m: Model) =
        let sgSurfaces = 
            m.scene.surfacesModel.sgSurfaces 
            |> HMap.toList 
            |> List.map snd
        
        match sgSurfaces |> List.tryHead with
        | Some v ->
            let fullBb = 
                sgSurfaces 
                |> List.map(fun x -> x.globalBB) 
                |> List.fold(fun a b -> Box3d.extendBy a b) v.globalBB

            // useful default viewpoint after 2nd import
            match m.scene.firstImport with                  
            | true -> 
                let refAction = ReferenceSystemApp.Action.InferCoordSystem(fullBb.Center)
                let (refSystem',_)= 
                    ReferenceSystemApp.update m.scene.config refConfig (m.scene.referenceSystem) refAction
                let navigation' =  { m.navigation with exploreCenter = fullBb.Center} 
                { m with 
                    navigation = navigation'
                    scene = { m.scene with referenceSystem = refSystem'; firstImport = false }
                } |> updateCameraUp |> lookAtBoundingBox v.globalBB
            | _-> m     
        | None -> m

    let isGrabbed (model: Model) =
        let sel = _selectedSurface.Get(model) |> Option.bind(fun x -> _sgSurfaces.Get(model).TryFind x)
        match sel with
        | Some s -> s.trafo.grabbed.IsSome
        | None -> false    

    let private animateFowardAndLocation (pos: V3d) (dir: V3d) (duration: RelativeTime) (name: string) = 
        {
            (CameraAnimations.initial name) with 
                sample = fun (localTime, globalTime) (state : CameraView) -> // given the state and t since start of the animation, compute a state and the cameraview
                if localTime < duration then                  
                    let rot      = Rot3d(state.Forward, dir) * localTime / duration
                    let forward' = rot.TransformDir(state.Forward)
                  
                    let vec       = pos - state.Location
                    let velocity  = vec.Length / duration                  
                    let dir       = vec.Normalized
                    let location' = state.Location + dir * velocity * localTime

                    let view = 
                        state 
                        |> CameraView.withForward forward'
                        |> CameraView.withLocation location'
      
                    Some (state,view)
                else None
        }

    let private createAnimation (pos: V3d) (forward: V3d) (animationsOld: AnimationModel) : AnimationModel =                                    
        animateFowardAndLocation pos forward 3.5 "ForwardAndLocation2s"
        |> AnimationAction.PushAnimation 
        |> AnimationApp.update animationsOld

    //TODO TO refactor ... move docking manipulation somewhere else... check what works and what doesn't
    let rec getAllDockElements (dnc: DockNodeConfig) : (list<DockElement>) = 
        match dnc with
        | DockNodeConfig.Vertical (weight,children) -> 
            let test = children |> List.map(fun x -> getAllDockElements x )
            test |> List.concat  
        | DockNodeConfig.Horizontal (weight,children) -> 
            let test = children |> List.map(fun x -> getAllDockElements x )
            test |> List.concat 
        | DockNodeConfig.Stack (weight,activeId,children) -> children 
        | DockNodeConfig.Element element -> [element] 
    
    let updateClosedPages (m: Model) (dncUpdated: DockNodeConfig) =
        let de = getAllDockElements m.scene.dockConfig.content
        let deUpdated = getAllDockElements dncUpdated
        let diff = ((Set.ofList de) - (Set.ofList deUpdated)) |> Set.toList
        // diff contains all changed elements (not only the deleted)
        match diff with
            | [] -> m.scene.closedPages
            | _ -> 
                let test = 
                    diff 
                    |> List.choose (fun x -> 
                        match deUpdated |> List.filter(fun y -> y.id = x.id) with
                        | [] -> Some x
                        | _  -> None)                
                List.append m.scene.closedPages test 
                   
    let private addDockElement (dnc: DockNodeConfig) (de: DockElement) = 
        match dnc with
        | DockNodeConfig.Vertical (weight,children) -> let add = List.append children [(Stack(weight, None, [de]))]
                                                       Horizontal(weight,add)
        | DockNodeConfig.Horizontal (weight,children) -> let add = List.append children [(Stack(weight, None, [de]))]
                                                         Horizontal(weight,add) 
        | DockNodeConfig.Stack (weight,activeId,children) -> Stack(weight, activeId, List.append [de] children)
        | DockNodeConfig.Element element ->  Stack(0.2, None, List.append [de] [element]) 

    let private createMultiSelectBox (startPoint: V2i) (viewPortSize: V2i) (currentPoint: V2i) =
        let clippingBox = Box2i.FromSize viewPortSize
        let newRenderBox = Box2i.FromPoints(clippingBox.Clamped(startPoint), clippingBox.Clamped(currentPoint)) // limited to rendercontrol-size!
                
        let ndc (v:V2i) = (((V2d v) / V2d viewPortSize) - V2d 0.5) * V2d(2.0, -2.0) // range [-1.0,1.0]
        let min = ndc newRenderBox.Min
        let max = ndc newRenderBox.Max
        let viewBox = Box3d.FromPoints(V3d(min, 0.0), V3d(max, 1.0))
        //Log.line "viewBox - min: %A\nviewBox - max:%A" viewBox.Min viewBox.Max

        (newRenderBox, viewBox)

    let private matchPickingInteraction (bc: BlockingCollection<string>) (p: V3d) (hitFunction:(V3d -> V3d option)) (surf: Surface) (m: Model) = 
        match m.interaction, m.viewerMode with
        | Interactions.DrawAnnotation, _ -> 
            let m = 
                match surf.surfaceType with
                | SurfaceType.SurfaceOBJ -> { m with drawing = { m.drawing with projection = Projection.Linear } } //TODO LF ... why is this happening?
                | _ -> m
            
            let view = 
                match m.viewerMode with 
                | ViewerMode.Standard -> m.navigation.camera.view
                | ViewerMode.Instrument -> m.scene.viewPlans.instrumentCam 

            let msg = Drawing.Action.AddPointAdv(p, hitFunction, surf.name)
            let drawing = DrawingApp.update m.scene.referenceSystem drawingConfig bc view m.drawing msg
            //Log.stop()
            { m with drawing = drawing } |> stash
        | Interactions.PlaceCoordinateSystem, ViewerMode.Standard -> 
                      
            let refAction = ReferenceSystemApp.Action.InferCoordSystem(p)
            let (refSystem',_) = 
                ReferenceSystemApp.update m.scene.config refConfig (m.scene.referenceSystem) refAction  
                      
            //let origin = new V3d(m.overlayFrustum.right * 0.5, m.overlayFrustum.bottom * 0.5, 1.0)
            //let os = { refSystem' with origin = origin }
            let m = { m with scene = { m.scene with referenceSystem = refSystem' }} 
            //update camera upvector
            updateCameraUp m
        | Interactions.PickExploreCenter, ViewerMode.Standard ->
            let c   = m.scene.config
            let ref = m.scene.referenceSystem
            let navigation' = 
                Navigation.update c ref navConf true m.navigation (Navigation.Action.ArcBallAction(ArcBallController.Message.Pick p))
            { m with navigation = navigation' }
        | Interactions.PlaceRover, ViewerMode.Standard ->
            let ref = m.scene.referenceSystem 

            let addPointMsg = ViewPlanApp.Action.AddPoint(p,ref,cache,(_surfacesModel.Get(m)))

            let outerModel, viewPlans' = 
              ViewPlanApp.update m.scene.viewPlans addPointMsg _navigation _footprint m.scene.scenePath m 

            let m' = 
                { m with 
                    scene = { m.scene with viewPlans = viewPlans'}  // CHECK-merge
                    footPrint = outerModel.footPrint 
                }
            match m.scene.viewPlans.working with
            | [] -> m'
            | _  -> { m' with tabMenu = TabMenu.Viewplanner }
        | Interactions.PlaceSurface, ViewerMode.Standard -> 
            let action = (SurfaceApp.Action.PlaceSurface(p)) 
            let surfaceModel =
                SurfaceApp.update 
                    m.scene.surfacesModel action m.scene.scenePath m.navigation.camera.view m.scene.referenceSystem
            { m with scene = { m.scene with surfacesModel = surfaceModel } }
        | Interactions.PickSurface, ViewerMode.Standard -> 
            let action = SurfaceApp.Action.GroupsMessage(GroupsAppAction.SingleSelectLeaf(list.Empty, surf.guid, ""))
            let surfaceModel' = 
                SurfaceApp.update
                   m.scene.surfacesModel action m.scene.scenePath m.navigation.camera.view m.scene.referenceSystem
            { m with scene = { m.scene with surfacesModel = surfaceModel' } }
        | Interactions.PickMinervaFilter, ViewerMode.Standard ->
            let action = PRo3D.Minerva.QueryAction.SetFilterLocation p |> PRo3D.Minerva.MinervaAction.QueryMessage
            let minerva = MinervaApp.update m.navigation.camera.view m.frustum m.minervaModel action
            { m with minervaModel = minerva }
        | Interactions.PickLinking, ViewerMode.Standard ->
            Log.startTimed "Pick Linking - filter"
            let filtered = m.minervaModel.session.filteredFeatures |> PList.map (fun f -> f.id) |> PList.toList |> HSet.ofList
            Log.stop()

            Log.startTimed "Pick Linking - checkPoint"
            let linkingAction, minervaAction = LinkingApp.checkPoint p filtered m.linkingModel
            Log.stop()

            Log.startTimed "Pick Linking - update minerva"
            let minerva' = MinervaApp.update m.navigation.camera.view m.frustum m.minervaModel minervaAction
            Log.stop()

            Log.startTimed "Pick Linking - update linking"
            let linking' = LinkingApp.update m.linkingModel linkingAction
            Log.stop()

            { m with linkingModel = linking'; minervaModel = minerva' }
        | Interactions.TrueThickness, ViewerMode.Standard -> m
        //    let msg = PlaneExtrude.App.Action.PointsMsg(Utils.Picking.Action.AddPoint p)
        //    let pe = PlaneExtrude.App.update m.scene.referenceSystem m.scaleTools.planeExtrude msg
        //    { m with scaleTools = { m.scaleTools with planeExtrude = pe  } }
        | _ -> m       

    let mutable lastHash = -1    
    let mutable rememberCam = FreeFlyController.initial.view

    let private shortFeedback (text: string) (m: Model) = 
        let feedback = {
            id      = System.Guid.NewGuid().ToString()
            text    = text
            timeout = 3000
            msg     = ViewerAction.NoAction ""
        }
        m |> UserFeedback.queueFeedback feedback

    let update 
        (runtime   : IRuntime) 
        (signature : IFramebufferSignature) 
        (sendQueue : BlockingCollection<string>) 
        (mailbox   : MessagingMailbox) 
        (m         : Model) 
        (msg       : ViewerAction) =
        //Log.line "[Viewer_update] %A inter:%A pick:%A" msg m.interaction m.picking
        match msg, m.interaction, m.ctrlFlag with
        | NavigationMessage  msg,_,false when (isGrabbed m |> not) && (not (AnimationApp.shouldAnimate m.animations)) ->                              
            let c   = m.scene.config
            let ref = m.scene.referenceSystem
            let nav = Navigation.update c ref navConf true m.navigation msg               
             
            //m.scene.navigation.camera.view.Location.ToString() |> NoAction |> ViewerAction |> mailbox.Post
             
            m 
            |> Lenses.set _navigation nav
            |> Lenses.set _animationView nav.camera.view
        | AnimationMessage msg,_,_ ->
            let a = AnimationApp.update m.animations msg
            { m with animations = a } |> Lenses.set _view a.cam
        | SetCamera cv,_,false -> _view.Set(m, cv)
        | SetCameraAndFrustum (cv, hfov, _),_,false -> m
        | SetCameraAndFrustum2 (cv,frustum),_,false ->
            let m = _view.Set(m, cv)
            { m with frustum = frustum }
        | AnnotationGroupsMessageViewer msg,_,_ ->
            let ag = m.drawing.annotations 
                
            { m with drawing = { m.drawing with annotations = GroupsApp.update ag msg}}
        | DrawingMessage msg,_,_-> //Interactions.DrawAnnotation
            match msg with
            | Drawing.FlyToAnnotation id ->
                let _a = m |> Lenses.get _flat |> HMap.tryFind id |> Option.map Leaf.toAnnotation
                match _a with 
                | Some a ->                                                
                    //m |> lookAtBoundingBox (Box3d(a.points |> PList.toList))
                    let animationMessage = 
                        animateFowardAndLocation a.view.Location a.view.Forward 2.0 "ForwardAndLocation2s"
                    let a' = AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
                    { m with  animations = a'}
                | None -> m
            | Drawing.PickAnnotation (hit,id) when m.interaction = Interactions.DrawLog && m.ctrlFlag ->
                match DrawingApp.intersectAnnotation hit id m.drawing.annotations.flat with
                | Some (anno, point) ->           
                    let pickingAction, msg =
                        match m.correlationPlot.logginMode, m.correlationPlot.correlationPlot.selectedLogNuevo with
                        | LoggingMode.PickReferencePlane, None ->
                            (CorrelationPanelsMessage.LogPickReferencePlane anno.key), "pick reference plane"
                        | LoggingMode.PickLoggingPoints, None ->
                            (CorrelationPanelsMessage.LogAddSelectedPoint(anno.key, point)), "add points to log"
                        | _, Some _->
                            (CorrelationPanelsMessage.LogAddPointToSelected(anno.key, point)), "changed log point"
                        | _ -> 
                            CorrelationPanelsMessage.Nop, ""
                    let cp = 
                        CorrelationPanelsApp.update
                            m.correlationPlot       
                            m.scene.referenceSystem
                            pickingAction
                                                        
                    { m with correlationPlot = cp } |> shortFeedback msg
                | None -> m
                
            | _ ->
                let view = 
                    match m.viewerMode with 
                    | ViewerMode.Standard -> m.navigation.camera.view
                    | ViewerMode.Instrument -> m.scene.viewPlans.instrumentCam

                let drawing = 
                    DrawingApp.update m.scene.referenceSystem drawingConfig sendQueue view m.drawing msg
                { m with drawing = drawing; } |> stash
        | SurfaceActions msg,_,_ ->
            let view = m.navigation.camera.view
            let s = SurfaceApp.update m.scene.surfacesModel msg m.scene.scenePath view m.scene.referenceSystem
            let animation = 
                match msg with
                | SurfaceApp.Action.FlyToSurface id -> 
                    let surf = m |> Lenses.get _sgSurfaces |> HMap.tryFind id
                    let surface = m.scene.surfacesModel.surfaces.flat |> HMap.find id |> Leaf.toSurface 
                    let superTrafo = PRo3D.Transformations.fullTrafo' surface m.scene.referenceSystem
                    match (surface.homePosition) with
                    | Some hp ->
                        //let trafo' = surface.preTransform.Forward * superTrafo.Forward
                        //let pos = trafo'.TransformPos(hp.Location)
                        //let forward = trafo'.TransformDir(hp.Forward)
                        let animationMessage = 
                            animateFowardAndLocation hp.Location hp.Forward 2.0 "ForwardAndLocation2s"
                        AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
                    | None ->
                        match surf with
                        | Some s ->
                            let bb = s.globalBB.Transformed(surface.preTransform.Forward * superTrafo.Forward)
                            let view = CameraView.lookAt bb.Max bb.Center m.scene.referenceSystem.up.value    
                            let animationMessage = 
                                animateFowardAndLocation view.Location view.Forward 2.0 "ForwardAndLocation2s"
                            let a' = AnimationApp.update m.animations (AnimationAction.PushAnimation(animationMessage))
                            a'
                        | None -> m.animations
                | _-> m.animations
                   
            { m with scene = { m.scene with surfacesModel = s}; animations = animation}
        | AnnotationMessage msg,_,_ ->                
            match m.drawing.annotations.singleSelectLeaf with
            | Some selected ->                             
                let f = (fun x ->
                    let a = x |> Leaf.toAnnotation
                    AnnotationProperties.update a msg |> Leaf.Annotations)
                      
                m.drawing.annotations
                |> Groups.updateLeaf selected f
                |> Lenses.set' _annotations m
            | None -> m       
        | BookmarkMessage msg,_,_ ->  
            let m', bm = Bookmarks.update m.scene.bookmarks msg _navigation m
            let animation = 
                match msg with
                | BookmarkAction.GroupsMessage k ->
                    match k with 
                    | GroupsAppAction.UpdateCam _->                      
                        createAnimation 
                            m'.navigation.camera.view.Location
                            m'.navigation.camera.view.Forward
                            m.animations                      
                    | _ -> m.animations
                | _ -> m.animations
            
            { m with scene = { m.scene with bookmarks = bm }; animations = animation} //; navigation = m'.scene.navigation }} 
        | BookmarkUIMessage msg,_,_ ->    
            let bm = GroupsApp.update m.scene.bookmarks msg
            { m with scene = { m.scene with bookmarks = bm }} 
        | RoverMessage msg,_,_ ->
            let roverModel = RoverApp.update m.scene.viewPlans.roverModel msg
            let viewPlanModel = ViewPlanApp.updateViewPlanFromRover roverModel m.scene.viewPlans
            { m with scene = { m.scene with viewPlans = viewPlanModel }}
        | ViewPlanMessage msg,_,_ ->
            let model, viewPlanModel = ViewPlanApp.update m.scene.viewPlans msg _navigation _footprint m.scene.scenePath m
            { m with 
                scene = { m.scene with viewPlans = viewPlanModel }
                footPrint = model.footPrint
            } 
        | DnSColorLegendMessage msg,_,_ ->
            let cm = FalseColorLegendApp.update m.drawing.dnsColorLegend msg
            { m with drawing = { m.drawing with dnsColorLegend = cm } }
        | ImportSurface sl,_,_ ->                 
            match sl with
            | [] -> m
            | paths ->
                let surfaces = 
                    paths 
                    |> List.filter (fun x -> Files.isSurfaceFolder x || Files.isZippedOpcFolder x)
                    |> List.map (SurfaceUtils.mk SurfaceType.SurfaceOPC m.scene.config.importTriangleSize.value)
                    |> PList.ofList

                let m = Scene.import' runtime signature surfaces m 
                m
                |> ViewerIO.loadLastFootPrint
                |> updateSceneWithNewSurface    
        | ImportDiscoveredSurfaces sl,_,_ -> 
            //"" |> UpdateUserFeedback |> ViewerAction |> mailbox.Post
            match sl with
            | [] -> m
            | paths ->
                //"Import OPCs..." |> UpdateUserFeedback |> ViewerAction |> mailbox.Post
                let selectedPaths = paths |> List.choose Files.tryDirectoryExists
                let surfacePaths = 
                    selectedPaths
                    |> List.map Files.superDiscoveryMultipleSurfaceFolder
                    |> List.concat

                let surfaces = 
                    surfacePaths 
                    |> List.filter (fun x -> Files.isSurfaceFolder x || Files.isZippedOpcFolder x)
                    |> List.map (SurfaceUtils.mk SurfaceType.SurfaceOPC m.scene.config.importTriangleSize.value)
                    |> PList.ofList

                    
                //gale crater hook
                let surfaces = GaleCrater.hack surfaces

                let m = Scene.import' runtime signature surfaces m 
                //updateSceneWithNewSurface m
                m
                |> ViewerIO.loadLastFootPrint
                |> updateSceneWithNewSurface
        | ImportDiscoveredSurfacesThreads sl,_,_ -> 
            let feedback = {
                id      = System.Guid.NewGuid().ToString()
                text    = "Importing OPCs..."
                timeout = 5000
                msg     = (ImportDiscoveredSurfaces sl)
            }
                
            m |> UserFeedback.queueFeedback feedback
        | ImportObject sl,_,_ -> 
            match sl |> List.tryHead with
            | Some path ->  
                let objects =                   
                    path 
                    |> SurfaceUtils.mk SurfaceType.SurfaceOBJ m.scene.config.importTriangleSize.value
                    |> PList.single                                
                m 
                |> Scene.importObj runtime signature objects 
                |> ViewerIO.loadLastFootPrint
                |> updateSceneWithNewSurface     
            | None -> m              
        | ImportAnnotationGroups sl,_,_ ->
            match sl |> List.tryHead with
            | Some path -> 
                //update groups
                let imported, flat, lookup = AnnotationGroupsImporter.startImporter path m.scene.referenceSystem

                let newGroups = 
                    m.drawing.annotations.rootGroup.subNodes
                    |> PList.append' imported

                let flat = 
                    flat 
                    |> HMap.map(fun k v -> 
                        let a = v |> Leaf.toAnnotation
                        let a' = if a.geometry = Geometry.DnS then { a with showDns = true } else a
                        a'|> Leaf.Annotations
                    )    
                                      
                let newflat = m.drawing.annotations.flat |> HMap.union flat

                //let inline median input = 
                //  let sorted = input |> Seq.toArray |> Array.sort
                //  let m1,m2 = 
                //      let len = sorted.Length-1 |> float
                //      len/2. |> floor |> int, len/2. |> ceil |> int 
                //  (sorted.[m1] + sorted.[m2] |> float)/2.

                //let stuff = 
                //  flat 
                //    |> HMap.toList 
                //    |> List.map snd 
                //    |> List.map Leaf.toAnnotation
                //    |> List.filter(fun x -> x.geometry = Geometry.DnS)
                //    |> List.map(fun x -> x.points |> DipAndStrike.calculateDnsErrors)
                //    |> List.concat
                  
                //let avg = stuff |> List.average
                //let med = stuff |> List.toArray |> median
                //let std = DipAndStrike.computeStandardDeviation avg stuff
                //Log.line "%f %f %f" std avg med
                  
                //let result = stuff |> List.map(fun x -> { error = x } )

                //let csvTable = Csv.Seq.csv ";" true id result
                //Csv.Seq.write ("./error.csv") csvTable |> ignore

                m 
                |> Lenses.set _groups newGroups
                |> Lenses.set _lookUp lookup
                |> Lenses.set _flat newflat          
            | None -> m
        | ImportSurfaceTrafo sl,_,_ ->  
            match sl |> List.tryHead with
            | Some path ->
                let imported = SurfaceTrafoImporter.startImporter path |> PList.toList                  
                let s = Surfaces.SurfaceApp.updateSurfaceTrafos imported m.scene.surfacesModel
                s.surfaces.flat |> HMap.toList |> List.iter(fun (_,v) -> Log.warn "%A" (v |> Leaf.toSurface).preTransform)
                m 
                |> Lenses.set _surfaceModelLens s  
            | None -> m
        | ImportRoverPlacement sl,_,_ ->  
            match sl |> List.tryHead with
            | Some path -> 
                let importedData = RoverPlacementImporter.startRPImporter path
                match m.scene.viewPlans.roverModel.selectedRover with
                | Some r -> 
                    let vp = ViewPlanApp.createViewPlanFromFile importedData m.scene.viewPlans r m.scene.referenceSystem m.navigation.camera
                    { m with scene = { m.scene with viewPlans = vp }}
                | None -> Log.error "no rover selected"; m
            | None -> m
        | QuickLoad1,_,_ ->
            Scene.loadScene @"E:\Aardwork\Exomars\_NEWVIEWER\Scenes\TestScene\scene.scn" m runtime signature     
        | QuickLoad2,_,_ ->
            (MeasurementsImporter.startImporter "E:\Aardwork\Exomars\_SCENES\cd - Copy\cd.xml") |> ignore               
            m
        | DeleteLast,_,_ -> 
            if File.Exists @".\last" then
                File.Delete(@".\last") |> ignore
                m
            else 
                m
        | PickSurface (p,name), _ ,true ->
            let fray = p.globalRay.Ray
            let r = fray.Ray
            let rayHash = r.GetHashCode()              

            let computeExactPick = true // CHECK-merge

            if computeExactPick then    // CHECK-merge

                let fray = p.globalRay.Ray
                let r = fray.Ray
                let rayHash = r.GetHashCode()
                
                if rayHash = lastHash then
                    Log.line "ray hash took over"
                    m
                else          
                    Log.startTimed "[PickSurface] try intersect kdtree of %s" name       
                         
                    let onlyActive (id : Guid) (l : Leaf) (s : SgSurface) = l.active
                    let onlyVisible (id : Guid) (l : Leaf) (s : SgSurface) = l.visible

                    let surfaceFilter = 
                        match m.interaction with
                        | Interactions.PickSurface -> onlyVisible
                        | _ -> onlyActive

                    let hitF (camLocation : V3d) (p : V3d) = 
                        let ray =
                            match m.drawing.projection with
                            | Projection.Viewpoint -> 
                                let dir = (p-camLocation).Normalized
                                FastRay3d(camLocation, dir)  
                            | Projection.Sky -> 
                                let up = m.scene.referenceSystem.up.value
                                FastRay3d(p + (up * 5000.0), -up)  
                            | _ -> Log.error "projection started without proj mode"; FastRay3d()
                   
                        match SurfaceApp.doKdTreeIntersection (_surfacesModel.Get(m)) m.scene.referenceSystem ray surfaceFilter cache with
                        | Some (t,surf), c ->                             
                            cache <- c; ray.Ray.GetPointOnRay t |> Some
                        | None, c ->
                            cache <- c; None
                                   
                    let result = 
                        match SurfaceApp.doKdTreeIntersection (_surfacesModel.Get(m)) m.scene.referenceSystem fray surfaceFilter cache with
                        | Some (t,surf), c ->                         
                            cache <- c
                            let hit = r.GetPointOnRay(t)
                            let cameraLocation = m.navigation.camera.view.Location 
                            let hitF = hitF cameraLocation
                   
                            lastHash <- rayHash
                            matchPickingInteraction sendQueue hit hitF surf m                                    
                        | None, _ -> 
                            Log.error "[PickSurface] no hit of %s" name
                            m

                    Log.stop()
                    Log.line "done intersecting"
                     
                    result
            else m
        | PickObject (p,id), _ ,_ ->  
            match m.picking with
            | true ->
                let hitF _ = None
                match (m.scene.surfacesModel.surfaces.flat.TryFind id) with
                | Some x -> matchPickingInteraction sendQueue p hitF (x |> Leaf.toSurface) m 
                | None -> m
            | false -> m
        | SaveScene s, _,_ ->                 
            let target = match m.scene.scenePath with | Some path -> path | None -> s
            m |> ViewerIO.saveEverything target
        | SaveAs s,_,_ ->
            ViewerIO.saveEverything s m
            |> ViewerIO.loadLastFootPrint
        | LoadScene path,_,_ ->                

            match SceneLoading.loadScene m runtime signature path with
                | SceneLoading.SceneLoadResult.Loaded(newModel,converted,path) -> 
                    Log.line "[PRo3D] loaded scene: %s" path
                    newModel
                | SceneLoading.SceneLoadResult.Error(msg,exn) -> 
                    Log.error "[PRo3D] could not load file: %s, error: %s" path msg
                    m

            |> ViewerIO.loadMinerva Scene.Minerva.defaultDumpFile Scene.Minerva.defaultCacheFile

        | NewScene,_,_ ->
            let initialModel = Viewer.initial m.messagingMailbox StartupArgs.initArgs //m.minervaModel.minervaMessagingMailbox
            { initialModel with recent = m.recent } |> ViewerIO.loadRoverData
        | KeyDown k, _, _ ->
            let m =
                match k with
                | Aardvark.Application.Keys.LeftShift -> { m with shiftFlag = true}
                | _ -> m
          
            let drawingAction =
                match k with
                | Aardvark.Application.Keys.Enter    -> Drawing.Action.Finish
                | Aardvark.Application.Keys.Back     -> Drawing.Action.RemoveLastPoint
                | Aardvark.Application.Keys.Escape   -> Drawing.Action.ClearWorking
                | Aardvark.Application.Keys.LeftCtrl -> 
                    match m.interaction with 
                    | Interactions.DrawAnnotation -> Drawing.Action.StartDrawing
                    | Interactions.PickAnnotation -> Drawing.Action.StartPicking
                    | _ -> Drawing.Action.Nop
                | Aardvark.Application.Keys.D0 -> Drawing.Action.SetSemantic Semantic.Horizon0
                | Aardvark.Application.Keys.D1 -> Drawing.Action.SetSemantic Semantic.Horizon1
                | Aardvark.Application.Keys.D2 -> Drawing.Action.SetSemantic Semantic.Horizon2
                | Aardvark.Application.Keys.D3 -> Drawing.Action.SetSemantic Semantic.Horizon3 
                | _  -> Drawing.Action.Nop

            let m =
                match k with 
                | Aardvark.Application.Keys.LeftCtrl ->
                    match m.interaction with
                    | Interactions.PickMinervaProduct -> 
                        { m with minervaModel = { m.minervaModel with picking = true }; ctrlFlag = true}
                    |_ -> { m with ctrlFlag = true}
                | _ -> m

            let m =
                match (m.ctrlFlag, k, m.scene.scenePath) with
                | true, Aardvark.Application.Keys.S, Some path -> 
                    { (ViewerIO.saveEverything path m) with ctrlFlag = false } |> shortFeedback "scene saved"
                | true, Aardvark.Application.Keys.S, None ->         
                    { m with ctrlFlag = false } |> shortFeedback "please use \"save\" in the menu to save the scene" 
                    // (saveSceneAndAnnotations p m)
                |_-> m
                         
            let m =
                match m.interaction with
                | Interactions.DrawAnnotation | Interactions.PickAnnotation ->
                    let view = m.navigation.camera.view
                    let m = { m with drawing = DrawingApp.update m.scene.referenceSystem drawingConfig sendQueue view m.drawing drawingAction } |> stash
                    match drawingAction with
                    | Drawing.Action.Finish -> { m with tabMenu = TabMenu.Annotations }
                    | _ -> m                     
                | _ -> m
                                    
            let m =
                match (m.interaction, k) with
                | Interactions.DrawAnnotation, _ -> m
                | _, Aardvark.Application.Keys.Enter -> 
                    let view = m.navigation.camera.view                                                                  
                    let minerva = MinervaApp.update view m.frustum m.minervaModel PRo3D.Minerva.MinervaAction.ApplyFilters
                    { m with minervaModel = minerva }
                | _ -> m

            let sensitivity = m.scene.config.navigationSensitivity.value
          
            let configAction = 
                match k with 
                | Aardvark.Application.Keys.PageUp   -> ConfigProperties.Action.SetNavigationSensitivity (Numeric.Action.SetValue (sensitivity + 0.5))
                | Aardvark.Application.Keys.PageDown -> ConfigProperties.Action.SetNavigationSensitivity (Numeric.Action.SetValue (sensitivity - 0.5))
                | _ -> ConfigProperties.Action.SetNavigationSensitivity (Numeric.Action.SetValue (sensitivity))

            let c' = ConfigProperties.update m.scene.config configAction

            let kind = 
                match k with
                | Aardvark.Application.Keys.F1 -> TrafoKind.Translate
                | Aardvark.Application.Keys.F2 -> TrafoKind.Rotate
                | _ -> m.trafoKind

            let m = { m with trafoKind = kind }

            //correlations
            let m = 
                match (k, m.interaction) with
                | (Aardvark.Application.Keys.Enter, Interactions.PickAnnotation) -> 
                        
                    let selected =
                        m.drawing.annotations.selectedLeaves 
                        |> HSet.map (fun x -> x.id)

                    let correlationPlot = 
                        CorrelationPanelsApp.update
                            m.correlationPlot 
                            m.scene.referenceSystem
                            (LogAssignCrossbeds selected)

                    { m with correlationPlot = correlationPlot; pastCorrelation = Some m.correlationPlot } |> shortFeedback "crossbeds assigned"                       
                | (Aardvark.Application.Keys.Enter, Interactions.DrawLog) -> 
                    //confirm when in logpick mode
                    let correlationPlot = 
                        CorrelationPanelsApp.update 
                            m.correlationPlot 
                            m.scene.referenceSystem
                            (UpdateAnnotations m.drawing.annotations.flat)
                                                                           
                    let correlationPlot, msg =
                        match m.correlationPlot.logginMode with
                        | LoggingMode.PickLoggingPoints ->                                                                  
                            CorrelationPlotAction.FinishLog
                            |> CorrelationPanelsMessage.CorrPlotMessage
                            |> CorrelationPanelsApp.update correlationPlot m.scene.referenceSystem, "finished log"                                
                        | LoggingMode.PickReferencePlane ->
                            correlationPlot, "reference plane selected"

                    let correlationPlot = 
                        CorrelationPanelsApp.update 
                            correlationPlot 
                            m.scene.referenceSystem
                            LogConfirm
                            
                    { m with correlationPlot = correlationPlot; pastCorrelation = Some m.correlationPlot } |> shortFeedback msg
                | (Aardvark.Application.Keys.Escape,Interactions.DrawLog) -> 
                    let panelUpdate = 
                        CorrelationPanelsApp.update 
                            m.correlationPlot
                            m.scene.referenceSystem
                            CorrelationPanelsMessage.LogCancel
                    { m with correlationPlot = panelUpdate } |> shortFeedback "cancel log"
                | (Aardvark.Application.Keys.Back, Interactions.DrawLog) ->                     
                    let panelUpdate = 
                        CorrelationPanelsApp.update
                            m.correlationPlot
                            m.scene.referenceSystem
                            CorrelationPanelsMessage.RemoveLastPoint
                    { m with correlationPlot = panelUpdate } |> shortFeedback "removed last point"
                | (Aardvark.Application.Keys.B, Interactions.DrawLog) ->                     
                    match m.pastCorrelation with
                    | None -> m
                    | Some past -> { m with correlationPlot = past; pastCorrelation = None} |> shortFeedback "undo last correlation"
                | _ -> m

            let m = 
                match k with 
                | Aardvark.Application.Keys.Space ->
                    //let wp = {
                    //    name = sprintf "wp %d" m.waypoints.Count
                    //    cv = (_camera.Get m).view
                    //}

                    Serialization.save "./logbrush" m.correlationPlot.logBrush |> ignore

                    //let waypoints = PList.append wp m.waypoints
                    //Log.line "saving waypoints %A" waypoints
                    //Serialization.save "./waypoints.wps" waypoints |> ignore
                    //{ m with waypoints = waypoints }                                                                                  
                    m |> shortFeedback "Saved logbrush"
                | Aardvark.Application.Keys.F8 ->
                    { m with scene = { m.scene with dockConfig = DockConfigs.core } }
                | _ -> m

            let interaction' = 
                match k with
                | Aardvark.Application.Keys.F1 -> Interactions.PickExploreCenter
                | Aardvark.Application.Keys.F2 -> Interactions.DrawAnnotation
                | Aardvark.Application.Keys.F3 -> Interactions.PickAnnotation
                | Aardvark.Application.Keys.F4 -> Interactions.PlaceCoordinateSystem
                //| Aardvark.Application.Keys.F6 -> Interactions.DrawLog
                | _ -> m.interaction
            { m with scene = { m.scene with config = c' }; interaction = interaction'}                               
        | KeyUp k, _,_ ->               
            let m =
                match k with
                | Aardvark.Application.Keys.LeftShift -> { m with shiftFlag = false}
                | _ -> m
            match k with
            | Aardvark.Application.Keys.LeftCtrl -> 
                match m.interaction with
                | Interactions.DrawAnnotation -> 
                    let view = m.navigation.camera.view
                    let d = DrawingApp.update m.scene.referenceSystem drawingConfig sendQueue view m.drawing Drawing.Action.StopDrawing
                    { m with drawing = d; ctrlFlag = false; picking = false }
                | Interactions.PickAnnotation -> 
                    let view = m.navigation.camera.view
                    let d = DrawingApp.update m.scene.referenceSystem drawingConfig sendQueue view m.drawing Drawing.Action.StopPicking 
                    { m with drawing = d; ctrlFlag = false; picking = false }
                | Interactions.PickMinervaProduct -> { m with minervaModel = { m.minervaModel with picking = false }}
                |_-> { m with ctrlFlag = false; picking = false }
            | _ -> m                                  
        | SetInteraction t,_,_ -> 
                
            // let feedback = sprintf "pick refrence plane; confirm with ENTER" t |> UserFeedback.create 3000
            //let feedback = "pick refrence plane \n confirm with ENTER" |> UserFeedback.create 3000

            { m with interaction = t } //|> UserFeedback.queueFeedback feedback
        | ReferenceSystemMessage a,_,_ ->                                
            let refsystem',_ = ReferenceSystemApp.update m.scene.config refConfig m.scene.referenceSystem a                
            let _refSystem = (Model.Lens.scene |. Scene.Lens.referenceSystem)
            let m' = m |> Lenses.set _refSystem refsystem'                        
            match a with 
            | ReferenceSystemApp.Action.SetUp _ | ReferenceSystemApp.Action.SetPlanet _ ->
                m' |> updateCameraUp
            | ReferenceSystemApp.Action.SetNOffset _ -> //update annotation results
                let flat = 
                    m'.drawing.annotations.flat
                    |> HMap.map(fun _ v ->
                        let a = v |> Leaf.toAnnotation
                        let results    = Calculations.recalcBearing a refsystem'.up.value refsystem'.northO  
                        let dnsResults = DipAndStrike.recalculateDnSAzimuth a refsystem'.up.value refsystem'.northO
                        { a with results = results; dnsResults = dnsResults } 
                        |> Leaf.Annotations
                    )
                m' 
                |> Lenses.set _flat flat                     
            | _ -> 
                m'
        | ConfigPropertiesMessage a,_,_ -> 
            //Log.line "config message %A" a
            let c' = ConfigProperties.update m.scene.config a
            let m = (Model.Lens.scene |. Scene.Lens.config).Set(m,c')
            
            match a with                   
            | ConfigProperties.Action.SetNearPlane _ | ConfigProperties.Action.SetFarPlane _ ->
                let fov = m.frustum |> Frustum.horizontalFieldOfViewInDegrees
                let asp = m.frustum |> Frustum.aspect
                let f' = Frustum.perspective fov c'.nearPlane.value c'.farPlane.value asp                    

                { m with frustum = f' }
                | _ -> m
        | SetMode d,_,_ -> 
            { m with trafoMode = d }
        | SetKind d,_,_ -> 
            { m with trafoKind = d }
        | TransformSurface (guid, trafo),_,_ ->
            //transformSurface m guid trafo //TODO moved function?
            m
        //| TransformAllSurfaces surfaceUpdates,_,_ -> //TODO MarsDL Hera
        //    match surfaceUpdates.IsEmptyOrNull () with
        //    | false ->
        //        transformAllSurfaces m surfaceUpdates
        //    | true ->
        //        Log.line "[Viewer] No surface updates found."
        //        m
        //| TransformAllSurfaces (surfaceUpdates,scs),_,_ ->
        //    match surfaceUpdates.IsEmptyOrNull () with
        //    | false ->
        //       //transformAllSurfaces m surfaceUpdates
        //       let ts = m.scene.surfacesModel.surfaces.activeGroup
        //       let action = SurfaceApp.Action.GroupsMessage(Groups.Groups.Action.ClearGroup ts.path)
        //       let surfM = SurfaceApp.update m.scene.surfacesModel action m.scene.scenePath m.scene.navigation.camera.view m.scene.referenceSystem 
        //       let m' = { m with scene = { m.scene with surfacesModel = surfM }}
        //       ViewerUtils.placeMultipleOBJs2 m' scs
        //    | true ->
        //        Log.line "[Viewer] No surface updates found."
        //        m
        | Translate (_,b),_,_ ->
            m
            //match _selectedSurface.Get(m) with
            //  | Some selected ->
            //    let sgSurf = m |> Lenses.get _sgSurfaces |> HMap.find selected.id
            //    let s' = { sgSurf with trafo = TranslateController.updateController sgSurf.trafo b }
                                        
            //    m 
            //    |> Lenses.get _sgSurfaces
            //    |> HMap.update selected.id (fun x -> 
            //        match x with 
            //            | Some _ -> printfn "%A" s'.trafo.previewTrafo.Forward.C3.XYZ; s'
            //            | None   -> failwith "surface not found")
            //    |> Lenses.set' _sgSurfaces m
                    
            //  | None -> m                               
        | Rotate (_,b),_,_ -> m
                //match _selectedSurface.Get(m) with
                //  | Some selected ->
                //    let sgSurf = m |> Lenses.get _sgSurfaces |> HMap.find selected.id
                //    let s' = { sgSurf with trafo = RotationController.updateController sgSurf.trafo b }

                //    m 
                //    |> Lenses.get _sgSurfaces
                //    |> HMap.update selected.id (fun x -> 
                //         match x with 
                //           | Some _ -> s'
                //           | None   -> failwith "surface not found")
                //    |> Lenses.set' _sgSurfaces m
                //  | None -> m
        | SetTabMenu tab,_,_ ->
            { m with tabMenu = tab }
        | SwitchViewerMode  vm ,_,_ -> 
            { m with viewerMode = vm }
        | OpenSceneFileLocation p,_,_ ->                
            let argument = sprintf "/select, \"%s\"" p
            Process.Start("explorer.exe", argument) |> ignore
            m
        | NoAction s,_,_ -> 
            if s.IsEmptyOrNull() |> not then 
                Log.line "[Viewer.fs] No Action %A" s
            m                   
        | UpdateDockConfig dcf,_,_ ->
            let closedPages = updateClosedPages m dcf.content
            { m with scene = { m.scene with dockConfig = dcf; closedPages = closedPages } }
        | AddPage de,_,_ -> 
            let closedPages = m.scene.closedPages |> List.filter(fun x -> x.id <> de.id)                
            let cont = addDockElement m.scene.dockConfig.content de
            let dockconfig = config {content(cont);appName "PRo3D"; useCachedConfig false }
            { m with scene = { m.scene with dockConfig = dockconfig; closedPages = closedPages } }
        | UpdateUserFeedback s,_,_ ->   { m with scene = { m.scene with userFeedback = s } }
        //| StartImportMessaging sl,_,_ -> 
        //    sl |> ImportDiscoveredSurfaces |> ViewerAction |> mailbox.Post
        //    { m with scene = { m.scene with userFeedback = "Import OPCs..." } }
        | Logging (text,message),_,_ ->  
            message |> MailboxAction.ViewerAction |> mailbox.Post
            { m with scene = { m.scene with userFeedback = text } }
        | ThreadsDone id,_,_ ->  
            { m with scene = { m.scene with userFeedback = ""; feedbackThreads = ThreadPool.remove id m.scene.feedbackThreads;} }
        | SnapshotThreadsDone id,_,_ ->  
            let _m = 
                { m with arnoldSnapshotThreads = ThreadPool.remove id m.arnoldSnapshotThreads }
            _m
            //  _m.shutdown ()
                
        | MinervaActions a,_,_ ->
            let currentView = m.navigation.camera.view
            match a with 
            | Minerva.MinervaAction.SelectByIds ids -> // AND fly to its center
                let minerva' = MinervaApp.update currentView m.frustum m.minervaModel a                                                                               
                // Fly to center of selected prodcuts
                let center = Box3d(minerva'.selectedSgFeatures.positions).Center
                let newForward = (center - currentView.Location).Normalized             
                { m with minervaModel = minerva'; animations = createAnimation center newForward m.animations }   
            | Minerva.MinervaAction.FlyToProduct v -> 
                let newForward = (v - currentView.Location).Normalized             
                { m with animations = createAnimation v newForward m.animations }   
            | _ ->                
                let minerva' = MinervaApp.update currentView m.frustum m.minervaModel a                                                                               
                //let linking' = PRo3D.Linking.LinkingApp.update currentView m.frustum injectLinking (PRo3D.Linking.LinkingAction(a))
                //MailboxAction.ViewerAction
                //ViewerAction.MinervaActions
                //a |> ViewerAction.MinervaActions |> MailboxAction.ViewerAction
                { m with minervaModel = minerva' }
        | LinkingActions a,_,_ ->
            match a with
            | PRo3D.Linking.LinkingAction.MinervaAction d ->
                { m with minervaModel = MinervaApp.update m.navigation.camera.view m.frustum m.minervaModel d }
            | PRo3D.Linking.LinkingAction.OpenFrustum d ->
                let linking' = PRo3D.Linking.LinkingApp.update m.linkingModel a         

                let camera' = { m.navigation.camera with view = CameraView.ofTrafo d.f.camTrafo }
                { m with navigation = { m.navigation with camera = camera' }; overlayFrustum = Some(d.f.camFrustum); linkingModel = linking' }
            | PRo3D.Linking.LinkingAction.CloseFrustum ->
                let linking' = PRo3D.Linking.LinkingApp.update m.linkingModel a
                //let camera' = { m.navigation.camera with view = rememberCam }
                { m with overlayFrustum = None; linkingModel = linking' } //navigation = { m.navigation with camera = camera' }}
            | _ -> 
                { m with linkingModel = PRo3D.Linking.LinkingApp.update m.linkingModel a }
        | OnResize a,_,_ ->              
            Log.line "[RenderControl Resized] %A" a
            { m with frustum = m.frustum |> Frustum.withAspect(float a.X / float a.Y) }
        | SetTextureFiltering b,_,_ ->
            {m with filterTexture = b}
       // | TestHaltonRayCasting _,_,_->
            //ViewerUtils.placeMultipleOBJs2 m [SnapshotShattercone.TestData] // TODO MarsDL put in own app
        //| UpdateShatterCones shatterCones,_,_ -> // TODO MarsDL put in own app
        //// TODO: LAURA
        //    match shatterCones.IsEmptyOrNull () with
        //    | false ->
        //        let m' = addOrClearSnapshotGroup m
        //        ViewerUtils.placeMultipleOBJs2 m' shatterCones
        //    | true ->
        //        Log.line "[Viewer] No shattercone updates found."
        //        m
        | StartDragging _,_,_
        | Dragging _,_,_ 
        | EndDragging _,_,_ -> 
            match m.multiSelectBox with
            | Some x -> { m with multiSelectBox = None }
            | None -> m
        | CorrelationPanelMessage a,_,_ ->
            let blurg =
                match a with 
                | CorrelationPanelsMessage.SemanticAppMessage _ ->
                    m |> PRo3D.ViewerIO.colorBySemantic'                    
                | _ -> 
                    m
            let blurg =
                { blurg with correlationPlot = CorrelationPanelsApp.update m.correlationPlot m.scene.referenceSystem a }

            let blarg = 
                match a with
                | CorrelationPanelsMessage.CorrPlotMessage 
                    (CorrelationPlotAction.DiagramMessage
                        (Svgplus.DA.DiagramAppAction.DiagramItemMessage
                            (diagramItemId, Svgplus.DiagramItemAction.RectangleStackMessage 
                                (_ , Svgplus.RectangleStackAction.RectangleMessage 
                                    (_, Svgplus.RectangleAction.Select rid)))))
                        ->          
                    Log.line "[Viewer] corrplotmessage %A" blurg.correlationPlot.correlationPlot.selectedFacies

                    let plot = blurg.correlationPlot.correlationPlot

                    let selectionSet =
                        match plot.selectedFacies with
                        | Some selected -> 
                            let selectedLogId : LogId = 
                                diagramItemId 
                                |> LogId.fromDiagramItemId

                            let log = 
                                plot.logsNuevo |> HMap.find selectedLogId


                            // find facies
                            match Facies.tryFindFacies selected log.facies with
                            | Some facies ->


                                // find measurements
                                Log.line "[Viewer] selected facies has %A measurements" facies.measurements

                                // also make for aggregation stuff ... secondary log
                                    

                                facies.measurements 
                                |> HSet.map(fun x -> 
                                    {
                                        id = ContactId.value x
                                        path = []
                                        name = ""
                                    }
                                )
                            | None ->
                                HSet.empty
                        | None -> 
                            HSet.empty

                    // m.drawing.annotations.selectedLeaves
                    { 
                        blurg with 
                            drawing = { 
                                blurg.drawing with 
                                    annotations = { 
                                        blurg.drawing.annotations with 
                                            selectedLeaves = selectionSet
                                    }
                            }
                    }
                | _ -> 
                    blurg

            blarg
        | PickSurface _,_,_ ->
            m
        | _ -> 
            Log.warn "[Viewer] don't know message %A. ignoring it." msg
            m                                            
        | _ -> m       
                                   
    let mkBrushISg color size trafo : ISg<Message> =
      Sg.sphere 5 color size 
        |> Sg.shader {
            do! Shader.stableTrafo
            do! DefaultSurfaces.vertexColor
            do! DefaultSurfaces.simpleLighting
        }
        |> Sg.noEvents
        |> Sg.trafo(trafo)
    
    let renderControlAttributes (m: MModel) = 
        let renderControlAtts (model: MNavigationModel) =
            amap {
                let! state = model.navigationMode
                match state with
                | NavigationMode.FreeFly -> 
                    yield! FreeFlyController.extractAttributes model.camera Navigation.Action.FreeFlyAction
                | NavigationMode.ArcBall ->                         
                    yield! ArcBallController.extractAttributes model.camera Navigation.Action.ArcBallAction
                | _ -> failwith "Invalid NavigationMode"
            } 
            |> AttributeMap.ofAMap |> AttributeMap.mapAttributes (AttributeValue.map NavigationMessage)   
        
        AttributeMap.unionMany [
            renderControlAtts m.navigation

            AttributeMap.ofList [
                attribute "style" "width:100%; height: 100%; float:left; background-color: #222222"
                attribute "data-samples" "4"
                attribute "useMapping" "true"
                //attribute "showFPS" "true"        
                //attribute "data-renderalways" "true"
                onKeyDown (KeyDown)
                onKeyUp   (KeyUp)        
                clazz "mainrendercontrol"
              //  onResize  (OnResize)
            ] 
        ]            

    let instrumentControlAttributes (m: MModel) = 
        AttributeMap.unionMany [
            AttributeMap.ofList [
                attribute "style" "width:100%; height: 100%; float:left; background-color: #222222"
                attribute "data-samples" "4"
                attribute "useMapping" "true"
                onKeyDown (KeyDown)
                onKeyUp (KeyUp)
            ] 
        ]     
        
    let allowAnnotationPicking (m : MModel) =       
        // drawing app needs pickable stuff. however whether annotations are pickable depends on 
        // outer application state. we consider annotations to pickable if they are visible
        // and we are in "pick annotation" mode.
        m.interaction |> Mod.map (function  
            | Interactions.PickAnnotation -> true
            | Interactions.DrawLog -> true
            | _ -> false
        )

    let allowLogPicking (m : MModel) =       
        // drawing app needs pickable stuff. however whether logs are pickable depends on 
        // outer application state. we consider annotations to pickable if they are visible
        // and we are in "pick annotation" mode.
        Mod.map2 (fun ctrlPressed interaction -> 
            match ctrlPressed, interaction with
            | true, Interactions.PickLog -> true
            | _ -> false
        ) m.ctrlFlag m.interaction

    let viewInstrumentView (m: MModel) = 
        let annotationsI, discsI = 
            DrawingApp.view 
                m.scene.config 
                mdrawingConfig 
                (m.scene.viewPlans.instrumentCam)
                (allowAnnotationPicking m)
                m.drawing

        let ioverlayed =
            let annos = 
                annotationsI
                |> Sg.map DrawingMessage
                |> Sg.fillMode (Mod.constant FillMode.Fill)
                |> Sg.cullMode (Mod.constant CullMode.None)

            [annos] |> Sg.ofList

        let discsInst = 
           discsI
             |> Sg.map DrawingMessage
             |> Sg.fillMode (Mod.constant FillMode.Fill)
             |> Sg.cullMode (Mod.constant CullMode.None)

        // instrument view control
        let icmds    = ViewerUtils.renderCommands m.scene.surfacesModel.sgGrouped ioverlayed discsInst m // m.scene.surfacesModel.sgGrouped overlayed discs m
        let icam = 
            Mod.map2 Camera.create (m.scene.viewPlans.instrumentCam) m.scene.viewPlans.instrumentFrustum
        DomNode.RenderControl((instrumentControlAttributes m), icam, icmds, None) //AttributeMap.Empty

    let viewRenderView (m: MModel) = 
        let annotations, discs = DrawingApp.view m.scene.config mdrawingConfig m.navigation.camera.view (allowAnnotationPicking m) m.drawing  
            
        let annotationSg = 
            let ds =
                discs
                |> Sg.map DrawingMessage
                |> Sg.fillMode (Mod.constant FillMode.Fill)
                |> Sg.cullMode (Mod.constant CullMode.None)

            let annos = 
                annotations
                |> Sg.map DrawingMessage
                |> Sg.fillMode (Mod.constant FillMode.Fill)
                |> Sg.cullMode (Mod.constant CullMode.None)

            let _, correlationPlanes =
                PRo3D.Correlations.CorrelationPanelsApp.viewWorkingLog 
                    m.scene.config.dnsPlaneSize.value
                    m.scene.cameraView 
                    m.scene.config.nearPlane.value 
                    m.correlationPlot 
                    m.drawing.dnsColorLegend

            let _, planes = 
                PRo3D.Correlations.CorrelationPanelsApp.viewFinishedLogs 
                    m.scene.config.dnsPlaneSize.value
                    m.scene.cameraView 
                    m.scene.config.nearPlane.value 
                    m.drawing.dnsColorLegend 
                    m.correlationPlot 
                    (allowLogPicking m)

            let viewContactOfInterest = 
                PRo3D.Correlations.CorrelationPanelsApp.viewContactOfInterest m.correlationPlot
                
            Sg.ofList[ds;annos; correlationPlanes; planes; viewContactOfInterest]

        let overlayed =
                        
            //let alignment = 
            //    AlignmentApp.view m.alignment m.scene.navigation.camera.view
            //        |> Sg.map AlignmentActions
            //        |> Sg.fillMode (Mod.constant FillMode.Fill)
            //        |> Sg.cullMode (Mod.constant CullMode.None)

            let near = m.scene.config.nearPlane.value

            let refSystem =
                ReferenceSystemApp.Sg.view
                    m.scene.config
                    mrefConfig
                    m.scene.referenceSystem
                    m.navigation.camera.view
                |> Sg.map ReferenceSystemMessage  

            let exploreCenter =
                Navigation.Sg.view m.navigation            
          
            let homePosition =
                Surfaces.Sg.viewHomePosition m.scene.surfacesModel
                                 
            let viewPlans =
                ViewPlanApp.Sg.view 
                    m.scene.config 
                    mrefConfig 
                    m.scene.viewPlans 
                    m.navigation.camera.view
                |> Sg.map ViewPlanMessage           

            let solText = 
                MinervaApp.getSolBillboards m.minervaModel m.navigation.camera.view near |> Sg.map MinervaActions

            let viewportFilteredText = 
                MinervaApp.viewPortLabels m.minervaModel m.navigation.camera.view (ViewerUtils.frustum m) |> Sg.map MinervaActions
                
            let correlationLogs, _ =
                PRo3D.Correlations.CorrelationPanelsApp.viewWorkingLog 
                    m.scene.config.dnsPlaneSize.value
                    m.scene.cameraView 
                    near 
                    m.correlationPlot 
                    m.drawing.dnsColorLegend

            let finishedLogs, _ =
                PRo3D.Correlations.CorrelationPanelsApp.viewFinishedLogs 
                    m.scene.config.dnsPlaneSize.value
                    m.scene.cameraView 
                    near 
                    m.drawing.dnsColorLegend 
                    m.correlationPlot 
                    (allowLogPicking m)

            [exploreCenter; refSystem; viewPlans; homePosition; solText; (correlationLogs |> Sg.map CorrelationPanelMessage); (finishedLogs |> Sg.map CorrelationPanelMessage)] |> Sg.ofList // (*;orientationCube*) //solText

        let minervaSg =
            let minervaFeatures = 
                MinervaApp.viewFeaturesSg m.minervaModel |> Sg.map MinervaActions 

            let filterLocation =
                MinervaApp.viewFilterLocation m.minervaModel |> Sg.map MinervaActions

            Sg.ofList [minervaFeatures] //;filterLocation]

        //let all = m.minervaModel.data.features
        let selected = 
            m.minervaModel.session.selection.highlightedFrustra
            |> AList.ofASet
            |> AList.toMod 
            |> Mod.map (fun x ->
                x
                |> PList.take 500
            )
            |> AList.ofMod
            |> ASet.ofAList
        
        let linkingSg = 
            PRo3D.Linking.LinkingApp.view 
                m.minervaModel.hoveredProduct 
                selected 
                m.linkingModel
            |> Sg.map LinkingActions

        let depthTested = 
            [linkingSg; annotationSg; minervaSg] |> Sg.ofList

        let cmds    = ViewerUtils.renderCommands m.scene.surfacesModel.sgGrouped overlayed depthTested m
        let frustum = Mod.map2 (fun o f -> o |> Option.defaultValue f) m.overlayFrustum m.frustum // use overlay frustum if Some()
        let cam     = Mod.map2 Camera.create m.navigation.camera.view frustum
        DomNode.RenderControl((renderControlAttributes m), cam, cmds, None)

    let view (m: MModel) = //(localhost: string)
       
        let myCss = [
            { kind = Stylesheet;  name = "semui";           url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.css" }
            { kind = Stylesheet;  name = "semui-overrides"; url = "semui-overrides.css" }
            { kind = Script;      name = "semui";           url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.js" }
        ]
        
        let bodyAttributes = [style "background: #1B1C1E; height:100%; overflow-y:scroll; overflow-x:hidden;"] //overflow-y : visible

        let onResize (cb : V2i -> 'msg) =
            onEvent "onresize" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

        let onFocus (cb : V2i -> 'msg) =
            onEvent "onfocus" ["{ X: $(document).width(), Y: $(document).height()  }"] (List.head >> Pickler.json.UnPickleOfString >> cb)

        let renderViewAttributes = [ 
            style "background: #1B1C1E; height:100%; width:100%"
            Events.onClick (fun _ -> SwitchViewerMode ViewerMode.Standard)            
            onResize OnResize     
            onFocus OnResize
            onMouseDown (fun button pos -> StartDragging (pos, button))
         //   onMouseMove (fun delta -> Dragging delta)
            onMouseUp (fun button pos -> EndDragging (pos, button))
        ]
        //let renderViewAttributes =
        //    amap {
        //        yield style "background: #1B1C1E; height:100%; width:100%"
        //        yield onClick (fun _ -> SwitchViewerMode ViewerMode.Standard)
        //    } |> AttributeMap.ofAMap
       
        let instrumentViewAttributes =
            amap {
                let! hor, vert = ViewPlanApp.getInstrumentResolution m.scene.viewPlans
                let height = "height:" + (vert/uint32(2)).ToString() + ";" ///uint32(2)
                let width = "width:" + (hor/uint32(2)).ToString() + ";" ///uint32(2)
                yield style ("background: #1B1C1E;" + height + width)
                yield Events.onClick (fun _ -> SwitchViewerMode ViewerMode.Instrument)
            } |> AttributeMap.ofAMap

        page (fun request -> 
            match Map.tryFind "page" request.queryParams with
            | Some "instrumentview" ->
                require (myCss) (
                    body [ style "background: #1B1C1E; width:100%; height:100%; overflow-y:auto; overflow-x:auto;"] [
                      Incremental.div instrumentViewAttributes (
                        alist {
                            yield viewInstrumentView m 
                            yield Viewer.Gui.textOverlaysInstrumentView m.scene.viewPlans
                        } )
                    ]
                )
            | Some "render" -> 
                require (myCss) (
                    body renderViewAttributes [ //[ style "background: #1B1C1E; height:100%; width:100%"] [
                        //div [style "background:#000;"] [
                        Incremental.div (AttributeMap.ofList[style "background:#000;"]) (
                            alist {
                                yield viewRenderView m
                                yield Viewer.Gui.textOverlays m.scene.referenceSystem m.navigation.camera.view
                                yield Viewer.Gui.textOverlaysUserFeedback m.scene
                                yield Viewer.Gui.dnsColorLegend m
                                yield Viewer.Gui.scalarsColorLegend m
                                yield Viewer.Gui.selectionRectangle m
                                yield PRo3D.Linking.LinkingApp.sceneOverlay m.linkingModel |> UI.map LinkingActions
                            }
                        )
                    ]                
                )
            | Some "surfaces" -> 
                require (myCss) (
                    body bodyAttributes
                        [SurfaceApp.surfaceUI m.scene.surfacesModel |> UI.map SurfaceActions] 
                )
            | Some "annotations" -> 
                require (myCss) (body bodyAttributes [Gui.Annotations.annotationUI m])
            | Some "bookmarks" -> 
                require (myCss) (body bodyAttributes [Gui.Bookmarks.bookmarkUI m])
            | Some "config" -> 
                require (myCss) (body bodyAttributes [Gui.Config.configUI m])
            | Some "viewplanner" -> 
                require (myCss) (body bodyAttributes [Gui.ViewPlanner.viewPlannerUI m])
            | Some "minerva" -> 
               //let pos = m.scene.navigation.camera.view |> Mod.map(fun x -> x.Location)
                let minervaItems = 
                    PRo3D.Minerva.MinervaApp.viewFeaturesGui m.minervaModel |> List.map (UI.map MinervaActions)

                let linkingItems =
                    [
                        Html.SemUi.accordion "Linked Products" "Image" false [
                            LinkingApp.viewSideBar m.linkingModel |> UI.map LinkingActions
                        ]
                    ]

                require (myCss @ Html.semui) (
                    body bodyAttributes (minervaItems @ linkingItems)
                )
            | Some "linking" ->
                require (myCss) (
                    body bodyAttributes [
                        LinkingApp.viewHorizontalBar m.minervaModel.session.selection.highlightedFrustra m.linkingModel |> UI.map LinkingActions
                    ]
                )
            | Some "corr_logs" ->
                CorrelationPanelsApp.viewLogs m.correlationPlot
                |> UI.map CorrelationPanelMessage
            | Some "corr_svg" -> 
                CorrelationPanelsApp.viewSvg m.correlationPlot
                |> UI.map CorrelationPanelMessage
            | Some "corr_semantics" -> 
                CorrelationPanelsApp.viewSemantics m.correlationPlot
                |> UI.map CorrelationPanelMessage
            | Some "corr_mappings" -> 
                require (myCss) (
                    body bodyAttributes [
                        CorrelationPanelsApp.viewMappings m.correlationPlot |> UI.map CorrelationPanelMessage
                    ] )
            | None -> 
                require (myCss) (
                    body [][                    
                        Gui.TopMenu.getTopMenu m
                        div[clazz "dockingMainDings"] [
                            m.scene.dockConfig
                            |> docking [                                           
                                style "width:100%; height:100%; background:#F00"
                                onLayoutChanged UpdateDockConfig ]
                        ]
                    ]
                )
            | _ -> body[][])                                            
                   
    let threadPool (m: Model) =
        let unionMany xs = List.fold ThreadPool.union ThreadPool.empty xs

        let drawing =
            DrawingApp.threads m.drawing |> ThreadPool.map DrawingMessage
       
        let animation = 
            AnimationApp.ThreadPool.threads m.animations |> ThreadPool.map AnimationMessage

        let nav =
            match m.navigation.navigationMode with
            | NavigationMode.FreeFly -> 
                FreeFlyController.threads m.navigation.camera
                |> ThreadPool.map Navigation.FreeFlyAction |> ThreadPool.map NavigationMessage
            | NavigationMode.ArcBall ->
                ArcBallController.threads m.navigation.camera
                |> ThreadPool.map Navigation.ArcBallAction |> ThreadPool.map NavigationMessage
            | _ -> failwith "invalid nav mode"
         
        let minerva = MinervaApp.threads m.minervaModel |> ThreadPool.map MinervaActions

        unionMany [drawing; animation; nav; m.scene.feedbackThreads; minerva]
        
    let loadWaypoints m = 
        match Serialization.fileExists "./waypoints.wps" with
        | Some path -> 
            let wp = Serialization.loadAs<plist<WayPoint>> path
            { m with waypoints = wp }
        | None -> m
    
    let start (runtime: IRuntime) (signature: IFramebufferSignature)(startEmpty: bool) messagingMailbox sendQueue dumpFile cacheFile =

        let m = 
            if startEmpty |> not then
                PRo3D.Viewer.Viewer.initial messagingMailbox StartupArgs.initArgs
                |> Scene.loadLastScene runtime signature
                |> Scene.loadLogBrush
                |> ViewerIO.loadRoverData                
                |> ViewerIO.loadAnnotations
                |> ViewerIO.loadCorrelations
                |> ViewerIO.loadLastFootPrint
                |> ViewerIO.loadMinerva dumpFile cacheFile
                |> ViewerIO.loadLinking                                       
            else
                PRo3D.Viewer.Viewer.initial messagingMailbox StartupArgs.initArgs |> ViewerIO.loadRoverData       

        App.start {
            unpersist = Unpersist.instance
            threads   = threadPool
            view      = view //localhost
            update    = update runtime signature sendQueue messagingMailbox
            initial   = m
        }