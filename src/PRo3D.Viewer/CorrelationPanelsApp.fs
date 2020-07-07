﻿namespace PRo3D.Correlations

open System
open System.Diagnostics

open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.Base.Incremental
open Aardvark.Base.Incremental.Operators
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.UI

open Aardvark.GeoSpatial.Opc

open UIPlus
open PRo3D
open PRo3D.Base.Annotation
open PRo3D.Viewer

open CorrelationDrawing
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.Model
open CorrelationDrawing.XXX
open CorrelationDrawing.LogTypes
open PRo3D.Base
open PRo3D.DrawingApp
open Aardvark.Rendering.Text
open Svgplus.DA
open Svgplus

module Conversion =
    let selectedPoints (points : hmap<Guid, LogPoint>) : hmap<AnnotationTypes.ContactId, V3d> =
        points 
        |> HMap.toList 
        |> List.map (fun (k,v) ->
            ContactId k, v.position )
        |> HMap.ofList
    
    //let geometry (g : Geometry) : SemanticTypes.GeometryType = 
    //    match g with
    //    | Geometry.Point    ->  SemanticTypes.GeometryType.Point
    //    | Geometry.Line     ->  SemanticTypes.GeometryType.Line
    //    | Geometry.Polyline ->  SemanticTypes.GeometryType.Polyline
    //    | Geometry.Polygon  ->  SemanticTypes.GeometryType.Polygon
    //    | Geometry.DnS      ->  SemanticTypes.GeometryType.DnS
    //    | _ -> g |> sprintf "not implemented %A" |> failwith 
    
    //let projection (g : Projection) : Projection = 
    //    match g with
    //    | Projection.Linear    ->  Types.Projection.Linear
    //    | Projection.Sky       ->  Types.Projection.Sky
    //    | Projection.Viewpoint ->  Types.Projection.Viewpoint
    //    | _ -> g |> sprintf "not implemented %A" |> failwith        

    let semanticsHack (thickness : float) (geometry : Geometry) : string =
        match geometry with
        | Geometry.Polyline | Geometry.DnS -> 
            match thickness with
            | 5.0 -> "Horizon0"
            | 4.0 -> "Horizon0"
            | 3.0 -> "Horizon0"
            | 2.0 -> "Horizon0"
            | 1.0 -> "Horizon1"
            | _ -> "couldn't be inferred"
        | _ -> "couldn't be inferred"
    
    let semanticsId (sem : SemanticId) = 
        let (SemanticId s) = sem                    
        if s.IsEmptyOrNull() then 
            failwith "ecountered empty semantic" 

        else s |> CorrelationDrawing.SemanticTypes.CorrelationSemanticId

    //let semanticsType (sem : SemanticType) : CorrelationDrawing.SemanticTypes.SemanticType =  
    //    sem |> int |> enum<CorrelationDrawing.SemanticTypes.SemanticType>
    
    let toContact (inAnno : Annotation) : CorrelationDrawing.AnnotationTypes.Contact = 
        //let semId = semanticsHack inAnno.thickness.value inAnno.geometry                
                
        let blurg : CorrelationDrawing.AnnotationTypes.Contact = {
            id            = inAnno.key        |> ContactId
            geometry      = inAnno.geometry   //|> geometry
            projection    = inAnno.projection //|> projection
            elevation     = (fun y -> (PRo3D.Base.CooTransformation.getAltitude y V3d.NaN PRo3D.Base.Planet.Mars) + 3000.0)
            semanticType  = inAnno.semanticType //|> semanticsType
            semanticId    = inAnno.semanticId   |> semanticsId

            selected      = false
            hovered       = false
                          
            points        = inAnno.points |> PList.map(fun x -> { point = x; selected = false } )            
                          
            visible       = inAnno.visible
            text          = inAnno.text
        }
      //  Log.line "[AnnoConversion] style: %A %A mapped: %A" inAnno.color inAnno.thickness.value blurg.semanticId.id
        blurg
    

module ContactsTable =
    let create (annotations : hmap<Guid, PRo3D.Groups.Leaf>) : CorrelationDrawing.AnnotationTypes.ContactsTable =     
        annotations 
        |> Groups.Leaf.toAnnotations
        |> HMap.toList
        |> List.map(fun (_,v) -> 
            let a = v |> Conversion.toContact
            a.id, a
        )
        |> HMap.ofList

    let add (contacts : ContactsTable) (annotations : hmap<Guid, PRo3D.Groups.Leaf>) : CorrelationDrawing.AnnotationTypes.ContactsTable =
        let k = annotations |> create
        contacts |> HMap.union k


module CorrelationPanelsApp =        
    open CorrelationDrawing.Nuevo

    let rand = Random () // todo orti : remove only for testing...

    let update 
        (m         : CorrelationPanelModel)
        (reference : PRo3D.ReferenceSystem.ReferenceSystem)
        (msg       : CorrelationPanelsMessage)
        : CorrelationPanelModel =

        match msg with
        | ExportLogs path ->
            
            let rows =
                m.correlationPlot.logsNuevo.Values 
                |> Seq.toList
                |> List.map(fun x -> 
                    let plane = x.referencePlane.plane

                    let sorted =
                        x.contactPoints.Values
                        |> Seq.toList
                        |> List.map (fun x -> plane.Height(x))
                        |> List.sort
                        
                    let thicknesses =
                        sorted 
                        |> List.pairwise
                        |> List.map (fun (h0,h1) -> (h0 - h1) |> abs) 
                        |> List.map (sprintf ",%f")
                        |> List.fold (+) String.Empty

                    x.name + thicknesses
                )                
                |> List.toArray

            File.writeAllLines path rows

            let argument = @"/select," + path
            Process.Start("explorer.exe", argument) |> ignore

            m
        | UpdateAnnotations annomap ->
            let annotations = annomap |> ContactsTable.add m.contacts
            Log.line "[CorrelationPanelsApp] updating annotations"
            { m with contacts = annotations }
        | CorrPlotMessage a -> 
            
            let selectedPoints, referencePlane, scale = 
                match m.logBrush with
                | Some brush -> 
                    match brush.referencePlane with
                    | Some p ->                        
                        brush.pointsTable |> Conversion.selectedPoints, p, brush.planeScale 
                    | None -> 
                        HMap.empty, DipAndStrikeResults.initial, Double.NaN
                | None -> 
                    HMap.empty, DipAndStrikeResults.initial, Double.NaN             
                
            let correlationPlot = 
                { 
                    m.correlationPlot with 
                        param_selectedPoints = selectedPoints
                        param_referencePlane = referencePlane 
                        param_referenceScale = scale
                }

            let updatedCorrlationPlot = CorrelationPlotApp.update m.contacts m.semanticApp reference.planet correlationPlot a

            let m = { m with correlationPlot = updatedCorrlationPlot; logBrush = None}

            match a with 
            | CorrelationPlotAction.DiagramMessage
                (Svgplus.DA.DiagramAppAction.DiagramItemMessage
                    (diagramItemId, Svgplus.DiagramItemAction.RectangleStackMessage 
                        (_ , Svgplus.RectangleStackAction.RectangleMessage 
                            (_, Svgplus.RectangleAction.Deselect)))) ->         
                // deselect contactOfInterests and SelectedFacies
                let selectedLogId = 
                    diagramItemId 
                    |> LogId.fromDiagramItemId
                
                let cp = 
                    m.correlationPlot 
                    |> CorrelationPlotApp.updateDiagramItemSelection' selectedLogId false

                let cp = { cp with selectedFacies = None }
                { m with 
                    contactOfInterest = HSet.empty
                    correlationPlot = cp
                }
            | CorrelationPlotAction.DiagramMessage
                (Svgplus.DA.DiagramAppAction.DiagramItemMessage
                    (diagramItemId, Svgplus.DiagramItemAction.RectangleStackMessage 
                        (_ , Svgplus.RectangleStackAction.RectangleMessage 
                            (_, Svgplus.RectangleAction.Select rid)))) -> 

                Log.warn "[todo orti]%A in %A" rid diagramItemId
                  
                //let rectangleIsSelected = 
                //    m.correlationPlot.diagram.rectanglesTable 
                //    |> HMap.tryFind rid 
                //    |> Option.map (fun x -> x.isSelected)
                //    |> Option.defaultValue false

                let selectedLogId = 
                    diagramItemId 
                    |> LogId.fromDiagramItemId

                //select log when rectangle/facies is selected
                let selectedFaciesId = 
                    m.correlationPlot.diagram.rectanglesTable 
                    |> HMap.tryFind rid 
                    |> Option.map(fun x ->
                        x.faciesId |> FaciesId)

                let dia = m.correlationPlot.diagram

                let selectedContacts =
                    match dia.selectedRectangle with
                    | Some rectId ->                         
                        //find the two rectangle borders
                        //let borders = 
                        dia.bordersTable.Values 
                            |> Seq.filter(fun x -> x.lowerRectangle = rectId || x.upperRectangle = rectId)            
                            |> Seq.map(fun x -> x.contactId |> LogToDiagram.toContactId)
                            |> HSet.ofSeq

                        //match borders with
                        //| Some (left, right)->
                        //    [left |> LogToDiagram.toContactId; right |> LogToDiagram.toContactId] |> HSet.ofList
                        //| None -> HSet.empty
                    | None -> HSet.empty                     

                let cp = 
                    m.correlationPlot 
                    |> CorrelationPlotApp.updateDiagramItemSelection' selectedLogId true

                let cp = { cp with selectedFacies = selectedFaciesId }

                { m with 
                    correlationPlot = cp
                    contactOfInterest = selectedContacts
                }
            | _ -> m
                       
        | SemanticAppMessage a ->                   
            { m with semanticApp = SemanticApp.update m.semanticApp a }
        | ColourMapMessage a ->
            
            let colorMap = 
                ColourMap.update m.correlationPlot.colorMap a      

            let cp =
                match m.correlationPlot.diagram.selectedRectangle with
                | Some r ->                 
                    CorrelationPlotApp.update 
                        m.contacts
                        m.semanticApp
                        reference.planet
                        m.correlationPlot
                        (GrainSizeTypeMessage (r,a))                
                | None ->
                    m.correlationPlot
                                          
            { m with correlationPlot = { cp with colorMap = colorMap } }
        | LogPickReferencePlane id when m.logginMode = LoggingMode.PickReferencePlane ->
            
            let contactId = ContactId id
            let contact = m.contacts |> HMap.find contactId

            let points = 
                contact.points
                |> PList.toList
                |> List.map(fun x -> x.point)                                
                                           
            let planeScale = 
                Calculations.getDistance (contact.points |> PList.map(fun x -> x.point) |> PList.toList) / 3.0

            let dns = 
                contact.points
                |> PList.map(fun x -> x.point) 
                |> DipAndStrike.calculateDipAndStrikeResults reference.up.value reference.north.value      
            
            let logBrush =
                {
                    pointsTable    = HMap.empty
                    localPoints    = PList.empty
                    modelTrafo     = Trafo3d.Identity
                    referencePlane = dns
                    planeScale     = planeScale
                } |> Some                        
                
            { m with logBrush = logBrush }    
            
        | LogAddSelectedPoint (id,p) when m.logginMode = LoggingMode.PickLoggingPoints ->
           let contactId = ContactId id
           let contact = m.contacts |> HMap.find contactId                        

           if (contact.semanticType <> SemanticType.Hierarchical) then
               Log.warn "[Correlations] can't pick non hierarchical annotation as log point"
               m
           else
               Log.line "[Correlations] picked logpoint at %A of %A" (contactId |> Contact.getLevelById) (contact.semanticId)
               let logPoint = { annoId = id; position = p }
               
               let logBrush =
                   m.logBrush
                   |> Option.map(fun b ->

                       //set mode trafo on first element
                       let modelTrafo = 
                           if b.pointsTable.IsEmpty then
                               logPoint.position |> Trafo3d.Translation
                           else
                               b.modelTrafo

                       let pointsTable = HMap.add id logPoint b.pointsTable
                       { 
                           b with
                             pointsTable = pointsTable
                             localPoints = pointsTable.Values |> PList.ofSeq
                             modelTrafo  = modelTrafo
                       }
                   )
                                                     
               { m with logBrush = logBrush }

        | LogAddPointToSelected (id,p) ->
            let contactId = ContactId id
            let contact = m.contacts |> HMap.find contactId                        

            if (contact.semanticType <> SemanticType.Hierarchical) then
                Log.warn "[Correlations] can't pick non hierarchical annotation as log point"
                m
            else
                Log.line "[Correlations] picked logpoint at %A of %A" (contactId |> Contact.getLevelById) (contact.semanticId)
                
                let contactId = id |> ContactId
                
                match m.correlationPlot.selectedLogNuevo with
                | Some selectedId ->                    
                    let selectedLog = m.correlationPlot.logsNuevo |> HMap.find selectedId
                    
                    //let selectedLog = 
                    //    { selectedLog with contactPoints = selectedLog.contactPoints |> HMap.alter contactId (fun _ -> Some p) }

                    let newLog = 
                        CorrelationDrawing.Nuevo.GeologicalLogNuevo.updateLogWithNewPoints 
                            m.contacts
                            m.semanticApp
                            reference.planet
                            (contactId,p)
                            selectedLog

                    let logs = 
                        m.correlationPlot.logsNuevo 
                        |> HMap.alter selectedId (function
                            | Some _ -> Some newLog
                            | None -> None
                        )                
                                                          
                    let plot = { m.correlationPlot with logsNuevo = logs }

                    let plot = //completely redraw the whole panel to trigger change
                        { plot with diagram = Svgplus.DiagramApp.init }
                        |> CorrelationPlotApp.reconstructDiagramsFromLogs
                            m.contacts
                            m.semanticApp
                            m.correlationPlot.colorMap

                    let diagram =
                        plot.diagram
                        |> Svgplus.DiagramApp.update (
                            Svgplus.DA.DiagramAppAction.SetYScaling(plot.diagram.yScaleValue))

                    let plot = { plot with diagram = diagram }

                    //update model
                    { m with correlationPlot = plot }
                | None -> m
        | RemoveLastPoint when m.logginMode = LoggingMode.PickLoggingPoints ->
            let logBrush = 
                match m.logBrush with
                | Some b ->
                    match b.localPoints |> PList.toList with
                    | [] -> failwith "[CorrelationPanel] empty brush shouldn't exist"
                    | _ :: [] -> None
                    | x :: xs -> 
                        Some { b with pointsTable = HMap.remove x.annoId b.pointsTable; localPoints = xs |> PList.ofList }
                | None -> None      
            { m with logBrush = logBrush }
        | LogCancel -> 
            match m.logginMode with
            | PickReferencePlane ->
                { m with logBrush = None }
            | PickLoggingPoints ->
                let brush = m.logBrush |> Option.map LogDrawingBrush.clearLogPoints
                { m with logBrush = brush; logginMode = PickReferencePlane }
            | EditLog -> m
        | LogConfirm ->
            match m.logginMode with
            | PickReferencePlane ->
                { m with logginMode = PickLoggingPoints }
            | PickLoggingPoints ->  // finish up and clear brush             
                { m with logBrush = None; logginMode = PickReferencePlane }
            | EditLog -> m
        | LogAssignCrossbeds selected ->
            Log.warn "selected %A" selected

            let log = 
                m.correlationPlot.selectedLogNuevo 
                |> Option.bind(fun x -> m.correlationPlot.logsNuevo |> HMap.tryFind x)

            let crossBeds =
                selected 
                |> HSet.choose (fun x -> 
                    let id = x |> ContactId
                    HMap.tryFind id m.contacts
                )
                |> HSet.filter(fun x -> x.semanticType = SemanticType.Angular)
                |> HSet.map(fun x -> x.id)
            
            match log, m.correlationPlot.selectedFacies with
            | Some l, Some faciesId ->
                //update facies
                let facies =
                    l.facies
                    |> Facies.updateFacies faciesId (fun x -> { x with measurements = crossBeds })

                //update log
                let l = { l with facies = facies }

                //update diagram
                //let blurg = //TODO TO refactor, probably put into correlation plot and hide behind event?
                //    CorrelationDrawing.CorrelationPlotApp.updateLogDiagram 
                //        m.contacts 
                //        m.semanticApp
                //        (m.correlationPlot |> CorrelationPlotApp.diagramConfigFrom)
                //        m.correlationPlot.colorMap
                //        l
                //        m.correlationPlot

                let plot = 
                    {
                        m.correlationPlot with 
                            logsNuevo =
                                m.correlationPlot.logsNuevo 
                                |> HMap.alter l.id (function | Some _ -> Some l | None -> None)
                    }                
                
                let plot = //completely redraw the whole panel to trigger change
                    { plot with diagram = Svgplus.DiagramApp.init }
                    |> CorrelationPlotApp.reconstructDiagramsFromLogs
                        m.contacts
                        m.semanticApp
                        m.correlationPlot.colorMap

                let diagram =
                    plot.diagram
                    |> Svgplus.DiagramApp.update (
                        Svgplus.DA.DiagramAppAction.SetYScaling(plot.diagram.yScaleValue))

                let plot = { plot with diagram = diagram }

                //update model
                { m with correlationPlot = plot }
                
            | _ ->                 
                m     
        | SetContactOfInterest _ -> 
            if m.contacts.IsEmpty = true then
                m
            else
                { m with contactOfInterest = m.contacts.Keys |> Seq.take 1 |> HSet.ofSeq }       
        | _ ->
            Log.warn "[CorrelationPanelsApp] unhandled action %A" msg
            m
     
    let view (m : MCorrelationDrawingModel) : DomNode<CorrelationPanelsMessage> = div [] []
    
    let viewMappings (m : MCorrelationPanelModel) : DomNode<CorrelationPanelsMessage> =
        ColourMap.view m.correlationPlot.colorMap
        |> UI.map CorrelationPanelsMessage.ColourMapMessage  
              
    let viewSemantics (m : MCorrelationPanelModel) : DomNode<CorrelationPanelsMessage> =
        SemanticApp.expertGUI m.semanticApp 
        |> UI.map CorrelationPanelsMessage.SemanticAppMessage
    
    let viewLogs (m : MCorrelationPanelModel) : DomNode<CorrelationPanelsMessage> =
        CorrelationPlotApp.listView m.correlationPlot
        |> UI.map CorrelationPanelsMessage.CorrPlotMessage
    
    let viewSvg (m : MCorrelationPanelModel) =      
        CorrelationPlotApp.viewSvg m.contacts m.correlationPlot 
        |> (UI.map CorrPlotMessage)

    let viewContactOfInterest (m : MCorrelationPanelModel) =
        m.contactOfInterest
        |> ASet.map (fun x -> 
            m.contacts 
            |> AMap.find x
            |> Mod.map (fun a -> 
                let points = a.points |> AList.map (fun p -> p.point) // global
                
                let modelTrafo =
                    points 
                    |> AList.toMod 
                    |> Mod.map (fun x ->
                        x 
                        |> PList.tryHead 
                        |> Option.map (fun h -> Trafo3d.Translation h)
                        |> Option.defaultValue Trafo3d.Identity)                               

                PRo3D.Base.OutlineEffect.createForLineOrPoint 
                    PRo3D.Base.OutlineEffect.PointOrLine.Line
                    (Mod.constant C4b.VRVisGreen) 
                    (Mod.constant 3.0)
                    5.0
                    RenderPass.main
                    modelTrafo 
                    points
            )
            |> Sg.dynamic
        )
        |> Sg.set                 

    let drawLogSg 
        (cam        : IMod<CameraView>) 
        (text       : IMod<string>)
        (near       : IMod<float>)
        (primary    : IMod<C4b>) 
        (secondary  : IMod<C4b>) 
        (dnsResults : IMod<option<MDipAndStrikeResults>>) 
        (modelTrafo : IMod<Trafo3d>) 
        (pickable   : Option<LogTypes.LogId * IMod<option<LogTypes.LogId>>>)
        (pickingAllowed: IMod<bool>) 
        (points     : alist<V3d>) =
      
        let elevationPoints =
            alist {                
                let! maybeDns = dnsResults

                match maybeDns with
                | Some dns -> 
                    let! plane = dns.plane

                    let sorted =
                        points 
                        |> AList.map(fun x -> x, plane.Height(x))
                        |> AList.sortBy snd
                    yield! sorted
                | None -> 
                    yield! AList.empty                
            }   
            
        let points' = elevationPoints |> AList.map fst
    
        //TODO TO: subtle incremental problem ... no clue why
        //let labels =
        //    elevationPoints //alist<position * elevation>
        //    |> AList.pairwise
        //    |> AList.map(fun ((p0,e0),(p1,e1)) ->
        //        let midPoint = ~~Trafo3d.Translation((p0 + p1) / 2.0)

        //        (e0 - e1)
        //        |> abs |> Formatting.Len |> string
        //        |> Mod.constant 
        //        |> billboardText cam midPoint
        //    ) 
        //    |> AList.toASet 
        //    |> Sg.set  
        
        let labels =
            elevationPoints.Content 
            |> Mod.map (fun l ->        
                    [ 
                        for ((p0,e0),(p1,e1)) in l |> PList.toList |> List.pairwise do
                            let midPoint = ~~((p0 + p1) / 2.0)

                            yield (e0 - e1)
                            |> abs |> Formatting.Len |> string
                            |> Mod.constant 
                            |> PRo3D.Base.Sg.billboardText cam midPoint
                    ] |> Sg.ofSeq
               )
            |> Sg.dynamic

        let labels2 =
            adaptive {
                let! points = elevationPoints.Content 
                let pairs = points |> PList.toList |> List.pairwise
                
                let labels =                    
                    pairs
                    |> List.map(fun ((p0,e0),(p1,e1)) ->
                        let midPoint = ~~((p0 + p1) / 2.0)
                        
                        (e0 - e1)
                        |> abs |> Formatting.Len |> string
                        |> Mod.constant 
                        |> PRo3D.Base.Sg.text cam near ~~60.0 midPoint (midPoint |> Mod.map Trafo3d.Translation) ~~0.05
                    ) |> Sg.ofSeq
                    
                return labels
            
            } |> Sg.dynamic

        //Sg.text view conf.nearPlane conf.hfov pos anno.modelTrafo text anno.textsize.value

        let polyLine = 
            match pickable with 
            | None -> 
                Sg.drawLines points' ~~0.001 secondary ~~5.0 modelTrafo
            | Some (logId, selectedLogId) -> 

                let logIsSelected = 
                    selectedLogId
                    |> Mod.map (function
                        | Some selId when selId = logId -> 
                            true
                        | _ -> 
                            false
                    )


                let event = 
                    SceneEventKind.Click, (fun _ -> 
                        let allowed = pickingAllowed |> Mod.force
                        match allowed with
                        | true ->  true, Seq.ofList[CorrPlotMessage(CorrelationPlotAction.SelectLogNuevo logId)]
                        | false -> true, Seq.empty)
                    
                let linesSg = Sg.pickableLine points' ~~0.001 secondary ~~5.0 modelTrafo true (fun lines -> event)
  
                let selectionSg = 
                    logIsSelected
                    |> Mod.map (function
                        | true ->
                            PRo3D.Base.OutlineEffect.createForLineOrPoint 
                                PRo3D.Base.OutlineEffect.PointOrLine.Both
                                (Mod.constant C4b.Yellow) 
                                (Mod.constant 5.0) 
                                3.0 
                                RenderPass.main 
                                modelTrafo points'
                        | false -> Sg.empty ) 
                    |> Sg.dynamic

                let labelSg = 
                    logIsSelected
                    |> Mod.map (function
                        | true -> labels2                            
                        | false -> Sg.empty
                    ) 
                    |> Sg.dynamic

                [ linesSg; labelSg; selectionSg ] |> Sg.ofList

        points 
        |> AList.map (fun x -> Sg.dot primary ~~8.0 ~~x)
        |> AList.toASet
        |> Sg.set            
        |> Sg.andAlso polyLine
        //|> Sg.andAlso labels2
    
    let viewWorkingLog (planeSize : IMod<float>) (cam : IMod<CameraView>) near (m : MCorrelationPanelModel) (falseColors : MFalseColorsModel) =
        
        let logSg =
            m.logBrush 
            |> Mod.map(fun x ->
                match x with
                | Some brush -> 
                    brush.localPoints 
                    |> AList.map(fun x -> x.position)
                    |> drawLogSg cam ~~"new log" near ~~C4b.Magenta ~~C4b.DarkMagenta brush.referencePlane brush.modelTrafo None (Mod.constant false) // cannot be selected
                | None -> Sg.empty           
            ) |> Sg.dynamic

        let planeSg = 
            m.logBrush 
            |> Mod.map(fun x ->
                match x with
                | Some brush ->                     
                    Sg.drawTrueThicknessPlane 
                        (brush.planeScale |> Mod.map2(fun a b -> a * b) planeSize) 
                        brush.referencePlane 
                        falseColors
                | None -> Sg.empty           
            ) |> Sg.dynamic
            
        logSg, planeSg
        
    let viewFinishedLogs (planeSize : IMod<float>) (cam : IMod<CameraView>) near (falseColors : MFalseColorsModel) (m : MCorrelationPanelModel) (pickingAllowed: IMod<bool>) =
        let logs = 
            m.correlationPlot.logsNuevo
            |> AMap.toASet
            |> ASet.map snd
                    
        let colors id =
            m.correlationPlot.selectedLogNuevo 
            |> Mod.map(function
                | Some selected when selected = id -> C4b.VRVisGreen, C4b.DarkCyan
                | _ -> C4b.Cyan, C4b.DarkCyan            
            )
        
        let logSg = 
            logs 
            |> ASet.map(fun x -> 

                let referencePlane = ~~(Some x.referencePlane)
                
                let points = 
                    x.contactPoints 
                    |> AMap.toASet 
                    |> ASet.map snd
                    |> ASet.toAList

                let trafo =            
                    points
                    |> AList.toMod 
                    |> Mod.map(fun y -> 
                        match y |> PList.tryHead with
                        | Some head -> Trafo3d.Translation head
                        | None -> Trafo3d.Identity
                    )

                let primary   = colors x.id |> Mod.map fst
                let secondary = colors x.id |> Mod.map snd

                points 
                |> drawLogSg 
                    cam 
                    ~~(x.id.ToString()) 
                    near
                    primary 
                    secondary 
                    referencePlane 
                    trafo 
                    (Some (x.id,  m.correlationPlot.selectedLogNuevo))
                    pickingAllowed
                
            ) |> Sg.set   
    
        let planesSg =
            aset {
                 let! selection = m.correlationPlot.selectedLogNuevo

                 let blu =
                    logs 
                    |> ASet.filter(fun x ->
                        match selection with
                        | Some s when s = x.id -> true                            
                        | _ -> false
                    )
                    |> ASet.map(fun x ->
                        let referencePlane = ~~(Some x.referencePlane)
                        Sg.drawTrueThicknessPlane 
                            ( x.planeScale |> Mod.map2(fun a b -> a * b) planeSize) 
                            referencePlane 
                            falseColors
                    )

                yield! blu
            } |> Sg.set  

        logSg, planesSg

    let viewExportLogButton (path : IMod<Option<string>>) =
        let blurg =
            adaptive{
                let! path = path
                match path with 
                | Some p -> return System.IO.Path.ChangeExtension(p,".csv") //p |> changeExtension ".pro3d.ann"
                | None -> return String.Empty
            }

        div [ clazz "ui inverted item"; onMouseClick (fun _ -> ExportLogs (blurg |> Mod.force))][
            text "Export Logs(*.csv)"
        ]