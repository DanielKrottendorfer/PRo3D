﻿namespace PRo3D.Bookmarkings

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Groups
open PRo3D.Navigation2

open PRo3D.Viewer

module Bookmarks = 
    
    let tryGet (bookmarks : plist<Bookmark>) key =
        bookmarks |> Seq.tryFind(fun x -> x.key = key)

    let getNewBookmark (camState : CameraView) (navigationMode : NavigationMode) (exploreCenter : V3d) (count:int) =
        let name = sprintf "Bookmark #%d" count //todo to make useful unique names
        {
            version        = Bookmark.current
            key            = System.Guid.NewGuid()
            name           = name
            cameraView     = camState
            navigationMode = navigationMode
            exploreCenter  = exploreCenter
        }
            
    let update (bookmarks : GroupsModel) (act : BookmarkAction) (navigationModel : Lens<'a,NavigationModel>) (outerModel:'a) : ('a * GroupsModel) =
        match act with
        | AddBookmark ->
            let nav = navigationModel.Get outerModel
            let newBm = 
                getNewBookmark nav.camera.view nav.navigationMode nav.exploreCenter bookmarks.flat.Count
            
            let groups = 
                GroupsApp.addLeafToActiveGroup (Leaf.Bookmarks newBm) true bookmarks
            
            outerModel, groups
        | GroupsMessage msg -> 
            match msg with
            | GroupsAppAction.UpdateCam id -> 
                let bm = bookmarks.flat |> HMap.tryFind id
                match bm with
                | Some b ->
                    match b with
                    | Leaf.Bookmarks bkm ->
                        let camState = FreeFlyController.initial

                        let nav' = 
                            {
                                camera = { camState with view = bkm.cameraView }
                                exploreCenter = bkm.exploreCenter
                                navigationMode = bkm.navigationMode
                            }
                        let newOuterModel = navigationModel.Set(outerModel, nav')
                        newOuterModel, bookmarks
                    | _ -> outerModel, bookmarks
                | None -> outerModel, bookmarks
            | _ -> 
                (outerModel, GroupsApp.update bookmarks msg)
        | PrintViewParameters key ->
            let optLeaf = bookmarks.flat |> HMap.tryFind key
            match optLeaf with
            | Some leaf ->
                match leaf with
                | Leaf.Bookmarks bm ->
                    Log.line "View parameters of %s" bm.name
                    Log.line "\"forward\": \"%s\"," (bm.cameraView.Forward.ToString ())
                    Log.line "\"location\": \"%s\"," (bm.cameraView.Location.ToString ())
                    Log.line "\"up\": \"%s\"" (bm.cameraView.Up.ToString ())
                    outerModel, bookmarks
                | _ -> outerModel, bookmarks
            | None ->  outerModel, bookmarks
                
    let mkColor (model : MGroupsModel) (b : MBookmark) =
        let id = b.key |> Mod.force

        let color =  
            model.selectedLeaves            
            |> ASet.map(fun x -> x.id = id)
            |> ASet.contains true
            |> Mod.map (fun x -> if x then C4b.Blue else C4b.White)
        
        color

    let lastSelected (model : MGroupsModel) (b : MBookmark) =
        let id = b.key |> Mod.force
        model.singleSelectLeaf 
        |> Mod.map(function
            | Some x -> x = id
            | _ -> false 
        )

    let isSingleSelect (model : MGroupsModel) (b : MBookmark) =
        model.singleSelectLeaf 
        |> Mod.map(function
            | Some selected -> selected = (b.key |> Mod.force)
            | None -> false )

    let viewBookmarks 
      (path         : list<Index>) 
      (model        : MGroupsModel) 
      (singleSelect : MBookmark*list<Index> -> 'outer) 
      (multiSelect  : MBookmark*list<Index> -> 'outer) 
      (lift         : GroupsAppAction -> 'outer) 
      (bookmarks    : alist<System.Guid>) : alist<DomNode<'outer>> =
        alist {
            let bookmarks = 
                bookmarks
                |> AList.filterM(fun x -> model.flat |> AMap.keys |> ASet.contains x)
                |> AList.map (fun x -> model.flat |> AMap.find x |> Mod.bind(id) |> Mod.force)
                |> AList.choose(fun x ->
                    match x with
                    | MBookmarks a -> Some a
                    | _ -> None )
            
            for b in bookmarks do
                              
                let! c = mkColor model b
                let bgc = sprintf "color: %s" (Html.ofC4b c)
                let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
                
                let singleSelect = fun _ -> singleSelect(b,path)
                let multiSelect  = fun _ -> multiSelect(b,path)
                let! selected = b |> isSingleSelect model
                let! key = b.key
                
                let headerColor = 
                   (isSingleSelect model b) 
                    |> Mod.map(fun x -> 
                    (if x then C4b.VRVisGreen else C4b.Gray) 
                        |> Html.ofC4b 
                        |> sprintf "color: %s"
                    )                 

                yield div [clazz "item"; style bgc] [
                    i [clazz "cube middle aligned icon"; onClick multiSelect;style bgc][] 
                    div [clazz "content"; style infoc] [
                        //let desc = b.name
                        yield Incremental.div (AttributeMap.ofList [style infoc])(
                            alist {
                                let! hc = headerColor
                                yield div [clazz "header"; style hc] [
                                    Incremental.span 
                                        ([ onClick singleSelect ]    |> AttributeMap.ofList)
                                        ([ Incremental.text b.name ] |> AList.ofList)
                                ]
                                yield i [clazz "home icon"; 
                                onClick (fun _ -> lift <| GroupsAppAction.UpdateCam key)][] |> UI.wrapToolTip "FlyTo" TTAlignment.Bottom                                          
                            } 
                        ) 
                        
                    ]
                ]
        }     
               

    let rec viewTree path (group : MNode) (model : MGroupsModel) : alist<DomNode<BookmarkAction>> =

        alist {

            let! active = model.activeGroup
            let color = sprintf "color: %s" (Html.ofC4b C4b.White)                
            

            let icon =
                adaptive {                    
                    let! group  =  group.key
                    return if (active.id = group) then "circle icon" else "circle thin icon"
                }
                      
            let map = 
                GroupsApp.clickIconAttributes 
                    icon 
                    (BookmarkAction.GroupsMessage(GroupsAppAction.SetActiveGroup (group.key |> Mod.force, path, group.name |> Mod.force)))
               
            let desc =
                div [style color] [       
                    Incremental.text group.name
                    Incremental.i map AList.empty |> UI.wrapToolTip "Set active" TTAlignment.Bottom
                        
                    i [clazz "plus icon"
                       onMouseClick (fun _ -> 
                         BookmarkAction.GroupsMessage(GroupsAppAction.AddGroup path))] [] 
                    |> UI.wrapToolTip "Add Group" TTAlignment.Bottom                             
                ]
                 
            let itemAttributes =
                amap {
                    yield onMouseClick (fun _ -> BookmarkAction.GroupsMessage(GroupsAppAction.ToggleExpand path))
                    let! selected = group.expanded
                    if selected 
                    then yield clazz "icon large outline open folder"
                    else yield clazz "icon large outline folder"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap
            
            let childrenAttribs =
                amap {
                    yield clazz "list"
                    let! isExpanded = group.expanded
                    if isExpanded then yield style "visible"
                    else yield style "hidden"
                }         

            let leafDomNodes = AList.collecti (fun i v -> viewTree (i::path) v model) group.subNodes    

            let singleSelect = 
                fun (a:MBookmark,path:list<Index>) -> 
                    BookmarkAction.GroupsMessage(GroupsAppAction.SingleSelectLeaf (path, a.key |> Mod.force, ""))

            let multiSelect = 
                fun (a:MBookmark,path:list<Index>) -> 
                    BookmarkAction.GroupsMessage(GroupsAppAction.AddLeafToSelection (path, a.key |> Mod.force, ""))

            let lift   = fun (a:GroupsAppAction) -> (BookmarkAction.GroupsMessage a)

            yield div [ clazz "item"] [
                Incremental.i itemAttributes AList.empty
                div [ clazz "content" ] [                         
                    div [ clazz "description noselect"] [desc]
                    Incremental.div (AttributeMap.ofAMap childrenAttribs) (                          
                        alist { 
                            let! isExpanded = group.expanded
                            if isExpanded then 
                                yield! leafDomNodes

                            if isExpanded then 
                                yield! viewBookmarks path model singleSelect multiSelect lift group.leaves
                        }
                    )  
                            
                ]
            ]
                
        }


    let viewBookmarksGroups (bookmarks : MGroupsModel) = 
        require GuiEx.semui (
            TreeView.view [] (viewTree [] bookmarks.rootGroup bookmarks)
        )                                    
 
    module UI =
        let view (model:MBookmark) =
            let view = model.cameraView
            require GuiEx.semui (
                Html.table [  
                    Html.row "Change Name:"[Html.SemUi.textBox model.name GroupsAppAction.SetChildName ]
                    Html.row "Pos:"     [Incremental.text (view |> Mod.map (fun x -> x.Location.ToString("0.00")))] 
                    Html.row "LookAt:"  [Incremental.text (view |> Mod.map (fun x -> x.Forward.ToString("0.00")))]
                    Html.row "Up:"      [Incremental.text (view |> Mod.map (fun x -> x.Up.ToString("0.00")))]
                    Html.row "Sky:"     [Incremental.text (view |> Mod.map (fun x -> x.Sky.ToString("0.00")))]
                ]
            )

        let viewGUI = 
         div [clazz "ui buttons inverted"] [
                    //onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                        button [clazz "ui icon button"; onMouseClick (fun _ -> AddBookmark )] [ //
                                i [clazz "plus icon"] [] ] |> UI.wrapToolTip "Add Bookmark" TTAlignment.Bottom
                   // )
                ] 

       