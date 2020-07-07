﻿namespace CorrelationDrawing

open Aardvark.Base.Rendering
open Aardvark.Base.Incremental
open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open UIPlus

open PRo3D.Base    
open PRo3D.Base.Annotation

open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.Types

type SemanticAction =
| SetSemantic       of option<CorrelationSemanticId>
| AddSemantic
| CancelNew
| SaveNew
| DeleteSemantic
| SemanticMessage   of CorrelationSemantic.Action
| SortBy        


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SemanticApp = 
              
    ///// convenience functions Semantics
    
    let getSelectedSemantic (app : SemanticsModel) =
        HMap.find app.selectedSemantic app.semantics
    
    let getSemantic (app : SemanticsModel) (semanticId : CorrelationSemanticId) =
        HMap.tryFind semanticId app.semantics
    
    let getSemanticOrDefault  (app : SemanticsModel) (semanticId : CorrelationSemanticId) =
        HMap.tryFind semanticId app.semantics
        |> Option.defaultValue CorrelationSemantic.initInvalid
    
    let getSemantic' (app : MSemanticsModel) (semanticId : CorrelationSemanticId) =
        AMap.tryFind semanticId app.semantics
    
    
    //let getSemanticOrDefault'  (app : MSemanticApp) (semanticId : SemanticId) =
    //  AMap.tryFind semanticId app.semantics
    
    let getColor (model : MSemanticsModel) (semanticId : IMod<CorrelationSemanticId>) =
        let sem = Mod.bind (fun id -> AMap.tryFind id model.semantics) semanticId
        Mod.bind (fun (se : option<MCorrelationSemantic>) ->
            match se with
            | Some s -> s.color.c
            | None -> Mod.constant C4b.Red) sem
    
    
    let getThickness (model : MSemanticsModel) (semanticId : IMod<CorrelationSemanticId>) =
        let sem = Mod.bind (fun id -> AMap.tryFind id model.semantics) semanticId
        Mod.bind (fun (se : option<MCorrelationSemantic>) ->
            match se with
            | Some s -> s.thickness.value
            | None -> Mod.constant 1.0) sem
    
    let getLabel (model : MSemanticsModel) (semanticId : IMod<CorrelationSemanticId>) = 
        let sem = Mod.bind (fun id -> AMap.tryFind id model.semantics) semanticId
        sem
        |> Mod.bind (fun x ->
            match x with 
            | Some s -> s.label.text
            | None -> Mod.constant "-NONE-")
     
    let getMetricSemantics (model : SemanticsModel) =
        model.semanticsList |> PList.filter (fun s -> s.semanticType = SemanticType.Metric)
    
    let getMetricId (model : SemanticsModel) =
        model 
        |> getMetricSemantics
        |> PList.tryAt 0
        |> Option.map (fun x -> x.id)
    
    
    ///// convenience functions II
    
    let next (e : SemanticsSortingOption) = 
        let plusOneMod (x : int) (m : int) = (x + 1) % m
        let eInt = int e
        enum<SemanticsSortingOption>(plusOneMod eInt 6) // hardcoded :(
    
    let setState (state : State) (s : option<CorrelationSemantic>)  = 
        (Option.map (fun x -> CorrelationSemantic.update x (CorrelationSemantic.SetState state)) s)
    
    let enableSemantic (s : option<CorrelationSemantic>) = 
        (Option.map (fun x -> CorrelationSemantic.update x (CorrelationSemantic.SetState State.Edit)) s)
    
    let disableSemantic (s : option<CorrelationSemantic>) = 
        (Option.map (fun x -> CorrelationSemantic.update x (CorrelationSemantic.SetState State.Display)) s)
    
    let disableSemantic' (s : CorrelationSemantic) =
        CorrelationSemantic.update s (CorrelationSemantic.SetState State.Display) 
        
    let sortFunction (sortBy : SemanticsSortingOption) = 
        match sortBy with
        | SemanticsSortingOption.Label        -> fun (x : CorrelationSemantic) -> x.label.text
        | SemanticsSortingOption.Level        -> fun (x : CorrelationSemantic) -> (sprintf "%03i" x.level.value)
//        | SemanticsSortingOption.GeometryType -> fun (x : Semantic) -> x.geometry.ToString()
        | SemanticsSortingOption.SemanticType -> fun (x : CorrelationSemantic) -> x.semanticType.ToString()
        | SemanticsSortingOption.SemanticId   -> fun (x : CorrelationSemantic) -> (x.id |> CorrelationSemanticId.value |> string)
        | SemanticsSortingOption.Timestamp    -> fun (x : CorrelationSemantic) -> x.timestamp
        | _                                   -> fun (x : CorrelationSemantic) -> x.timestamp
    
    let getSortedList 
        (list    : hmap<CorrelationSemanticId, CorrelationSemantic>) 
        (sortBy  : SemanticsSortingOption) =

        DS.HMap.toSortedPlist list (sortFunction sortBy)
    
    let deleteSemantic (model : SemanticsModel)=
        let getAKey (m : hmap<CorrelationSemanticId, 'a>) =
            m |> HMap.toSeq |> Seq.map fst |> Seq.tryHead
    
        let rem =
            model.semantics
            |> HMap.remove model.selectedSemantic
    
        match getAKey rem with
        | Some k  -> 
          let updatedSemantics = (rem |> HMap.alter k enableSemantic)
          {model with 
            semantics = updatedSemantics 
            semanticsList = getSortedList updatedSemantics model.sortBy
            selectedSemantic = k
          }
        | None   -> model
    
    let insertSemantic (s : CorrelationSemantic) (state : State) (model : SemanticsModel) = 
        let newSemantics = 
            (model.semantics.Add(s.id, s)
            |> HMap.alter model.selectedSemantic disableSemantic
            |> HMap.alter s.id (setState state))
    
        {
            model with 
                selectedSemantic  = s.id
                semantics         = newSemantics
                semanticsList     = getSortedList newSemantics model.sortBy
        }
    
    
    let insertSampleSemantic (model : SemanticsModel) = 
        let id = System.Guid.NewGuid().ToString()
        let newSemantic = 
            CorrelationSemantic.Lens._labelText.Set((CorrelationSemantic.initial id),"NewSemantic")
        insertSemantic newSemantic State.New model
    
    let getInitialWithSamples =
        SemanticsModel.initial
        |> insertSemantic (CorrelationSemantic.initialHorizon0   ("Horizon0")) State.Display
        |> insertSemantic (CorrelationSemantic.initialHorizon1   ("Horizon1")) State.Display
        |> insertSemantic (CorrelationSemantic.initialHorizon2   ("Horizon2")) State.Display
        |> insertSemantic (CorrelationSemantic.initialHorizon3   ("Horizon3")) State.Display
        |> insertSemantic (CorrelationSemantic.initialHorizon4   ("Horizon4")) State.Display
        |> insertSemantic (CorrelationSemantic.initialCrossbed   ("Crossbed")) State.Display
        |> insertSemantic (CorrelationSemantic.impactBreccia     ("Impact")) State.Display
        |> insertSemantic (CorrelationSemantic.initialGrainSize2 ("GrainSize")) State.Display
    
    let deselectAllSemantics (semantics : hmap<CorrelationSemanticId, CorrelationSemantic>) =
        semantics |> HMap.map (fun k s -> disableSemantic' s)
              
    ////// UPDATE 
    let update (model : SemanticsModel) (action : SemanticAction) =
        match (action, model.creatingNew) with 
        | SetSemantic sem, false ->
            match sem with
            | Some s  ->
                let updatedSemantics = 
                    model.semantics
                    |> HMap.alter model.selectedSemantic disableSemantic
                    |> HMap.alter s enableSemantic
                        
                {
                    model with 
                        selectedSemantic  = s
                        semanticsList     = getSortedList updatedSemantics model.sortBy 
                        semantics         = updatedSemantics
                }
            | None    -> model
        
        | SemanticMessage sem, _   ->
            let fUpdate (semO : Option<CorrelationSemantic>) = 
                match semO with
                | Some s  -> Some(CorrelationSemantic.update s sem)
                | None    -> None

            let updatedSemantics = HMap.alter model.selectedSemantic fUpdate model.semantics

            {
              model with 
                  semantics     = updatedSemantics
                  semanticsList = getSortedList updatedSemantics model.sortBy
            }
        
        | AddSemantic, false     -> 
            {insertSampleSemantic model with creatingNew = true}
            
        | DeleteSemantic, false  -> deleteSemantic model
        | SortBy, false          ->
            let newSort = next model.sortBy
            {
                model with 
                    sortBy = newSort
                    semanticsList = 
                        model.semanticsList
                        |> PList.toSeq
                        |> Seq.sortBy (sortFunction newSort)
                        |> PList.ofSeq
            }                   
        | SaveNew, true   -> 
            let updatedSemantics = 
                model.semantics
                |> HMap.alter model.selectedSemantic enableSemantic

            {
                model with 
                    creatingNew   = false
                    semanticsList = getSortedList updatedSemantics model.sortBy 
                    semantics     = updatedSemantics
            }
        | CancelNew, true -> 
            { deleteSemantic model with creatingNew = false }
        | _ -> model
    
    let save (savename : string) (model : SemanticsModel) =
        
        Serialization.saveJson (sprintf "%s%s" "./" savename) model.semantics |> ignore
        Log.line "[Semantics] saving semantics: %s" savename
        model
    
    let load (savename : string) (model : SemanticsModel) =
                
        let semantics = Serialization.loadJsonAs savename
        
        Log.line "[Semantics] loading semantics" 
        let newModel =
            match HMap.isEmpty semantics with
            | true  -> getInitialWithSamples
            | _     ->
                let deselected = deselectAllSemantics semantics
                let upd =
                    {
                        model with 
                            semantics        = deselected
                            semanticsList    = getSortedList deselected model.sortBy
                    }
                update upd (SemanticAction.SetSemantic ((upd.semanticsList.TryGet 0) |> Option.map (fun s -> s.id)))
        newModel
        
                    
  ///////////////////////////////// VIEW ///////////////////  
    let simpleView (model : MSemanticsModel) =
        let domList = 
            alist {                 
                for mSem in model.semanticsList do
                    let! state = mSem.state
                     // match state with 
                    let! col = mSem.color.c
                    let textCol = Tables.textColorFromBackground col
                    let st = 
                        match state with
                        | State.Display -> []
                        | State.Edit | State.New ->
                            [style (sprintf "background: %s;%s" (GUI.CSS.colorToHexStr col) textCol)]
                     
                    let domNodes = CorrelationSemantic.View.miniView mSem
                    let domNodes = domNodes |> List.map (UI.map SemanticMessage)
                    
                    
                    yield tr 
                        (st@[onClick (fun _ -> SetSemantic (Some mSem.id))])
                        domNodes
            } 
          
        Tables.toTableView (div[][]) domList ["Annotation Type"]

    let expertGUI (model : MSemanticsModel) = 
      let menu = 
        div [clazz "ui horizontal inverted menu";
             style "width:100%; height: 10%; float:middle; vertical-align: middle"][
          div [clazz "item"]
              [button [clazz "ui small icon button"; onMouseClick (fun _ -> AddSemantic)] 
                      [i [clazz "small plus icon"] [] ] |> UIPlus.ToolTips.wrapToolTip "add"];
          div [clazz "item"]
              [button [clazz "ui small icon button"; onMouseClick (fun _ -> DeleteSemantic)] 
                      [i [clazz "small minus icon"] [] ] |> UIPlus.ToolTips.wrapToolTip "delete"];
          div [clazz "item"] [
            button 
              [clazz "ui small icon button"; style "width: 20ch; text-align: left"; onMouseClick (fun _ -> SortBy;)]
              [Incremental.text (Mod.map (fun x -> sprintf "sort: %s" (string x)) model.sortBy)]
          ]  
        ]
    
      let domList = 
        alist {                 
          for mSem in model.semanticsList do
            let! state = mSem.state
            if state = State.New then 
              let! domNode = CorrelationSemantic.View.view mSem
              let menu = Menus.saveCancelMenu SaveNew CancelNew

              yield Tables.intoActiveTr 
                      (SetSemantic (Some mSem.id))
                      (domNode |> List.map (fun x -> x |> UI.map SemanticMessage)) 
                     
              yield Tables.intoTr [(Tables.intoTd' menu domNode.Length)]   
                  
            else if state = State.Edit then
              let! domNode = CorrelationSemantic.View.view mSem    
              let! col = mSem.color.c
              let textCol = Tables.textColorFromBackground col
              let st = style (sprintf "background: %s;%s" (GUI.CSS.colorToHexStr col) textCol)
              yield tr 
                      ([st; onClick (fun str -> SetSemantic (Some mSem.id))]) 
                      (List.map (fun x -> x |> UI.map SemanticMessage) domNode)
            else 
              let! domNode = CorrelationSemantic.View.view mSem           
              yield tr 
                      ([onClick (fun str -> SetSemantic (Some mSem.id))]) 
                      (List.map (fun x -> x |> UI.map SemanticMessage) domNode)
        } 

      Tables.toTableView menu domList ["Label";"Weight";"Colour";"Level";"Semantic Type";"Geometry"]

    let threads _ =
      ThreadPool.empty
    
    let app : App<SemanticsModel, MSemanticsModel, SemanticAction> =
        {
            unpersist = Unpersist.instance
            threads   = threads
            initial   = getInitialWithSamples
            update    = update
            view      = expertGUI
        }
    
    let start () = App.start app

