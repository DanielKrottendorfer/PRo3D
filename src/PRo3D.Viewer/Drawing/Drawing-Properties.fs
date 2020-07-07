﻿namespace PRo3D

open Aardvark.Base
open System
open Aardvark.UI
open Aardvark.UI.Static.Svg
open Aardvark.Base.Incremental
open PRo3D.Groups
open PRo3D.Base.Annotation

module AnnotationProperties = 
            
    type Action = 
        | SetGeometry     of Geometry
        | SetProjection   of Projection
        | SetSemantic     of Semantic
        | ChangeThickness of Numeric.Action
        | ChangeColor     of ColorPicker.Action
        | SetText         of string
        | SetTextSize     of Numeric.Action
        | ToggleVisible
        | ToggleShowDns
        | PrintPosition   
       
    let horizontalDistance (points:list<V3d>) (up:V3d) = 
            match points.Length with
              | 1 -> 0.0
              | _ -> let v = points.[0] - points.[points.Length - 1]
                     Math.Sqrt(v.LengthSquared - (v * up.Normalized).LengthSquared)
                    // Math.Sqrt(Math.Pow(v.Length, 2.0) + Math.Pow((v * up.Normalized).Length, 2.0))

    let verticalDistance (points:list<V3d>) (up:V3d) = 
        match points.Length with
            | 1 -> 0.0
            | _ -> let v = points.[0] - points.[points.Length - 1]
                   (v * up.Normalized).Length
            
    let update (model : Annotation) (act : Action) =
        match act with
            | SetGeometry mode ->
                { model with geometry = mode }
            | SetSemantic mode ->
                { model with semantic = mode }
            | SetProjection mode ->
                { model with projection = mode }
            | ChangeThickness a ->
                { model with thickness = Numeric.update model.thickness a }
            | SetText t ->
                { model with text = t }
             | SetTextSize s ->
                { model with textsize = Numeric.update model.textsize s }
            | ToggleVisible ->
                { model with visible = (not model.visible) }
            | ToggleShowDns ->
                { model with showDns = (not model.showDns) }
            | ChangeColor a ->
                { model with color = ColorPicker.update model.color a }
            | PrintPosition ->
                let p = match model.geometry with
                          | Geometry.Point -> (model.points |> PList.tryHead).Value.ToString()
                          | _-> ""
                Log.line "Position: %A" p
                model

    let view (model : MAnnotation) = 

        require GuiEx.semui (
            Html.table [                                            
                Html.row "Geometry:"    [Incremental.text (model.geometry |> Mod.map (fun x -> sprintf "%A" x ))]
                Html.row "Projection:"  [Incremental.text (model.projection |> Mod.map (fun x -> sprintf "%A" x ))]
                Html.row "Semantic:"    [Html.SemUi.dropDown model.semantic SetSemantic]      
                Html.row "Thickness:"   [Numeric.view' [InputBox] model.thickness |> UI.map ChangeThickness ]
                Html.row "Color:"       [ColorPicker.view model.color |> UI.map ChangeColor ]
                Html.row "Text:"        [Html.SemUi.textBox model.text SetText ]
                Html.row "TextSize:"    [Numeric.view' [InputBox] model.textsize |> UI.map SetTextSize ]
                Html.row "Visible:"     [GuiEx.iconCheckBox model.visible ToggleVisible ]
                Html.row "Show DnS:"    [GuiEx.iconCheckBox model.showDns ToggleShowDns ]
            ]

        )

    let viewResults (model : MAnnotation) (up:IMod<V3d>) =   
        
        let height   = Mod.bindOption model.results Double.NaN (fun a -> a.height)
        let heightD  = Mod.bindOption model.results Double.NaN (fun a -> a.heightDelta)
        let alt      = Mod.bindOption model.results Double.NaN (fun a -> a.avgAltitude)
        let length   = Mod.bindOption model.results Double.NaN (fun a -> a.length)
        let wLength  = Mod.bindOption model.results Double.NaN (fun a -> a.wayLength)
        let bearing  = Mod.bindOption model.results Double.NaN (fun a -> a.bearing)
        let slope    = Mod.bindOption model.results Double.NaN (fun a -> a.slope)

        let pos = Mod.map( fun x -> match x with 
                                        | Geometry.Point -> let points = model.points |> AList.toList
                                                            points.[0].ToString()
                                        | _-> "" ) model.geometry
        
        let vertDist = Mod.map( fun u -> verticalDistance   (model.points |> AList.toList) u ) up
        let horDist  = Mod.map( fun u -> horizontalDistance (model.points |> AList.toList) u ) up
      
        require GuiEx.semui (
          Html.table [   
            Html.row "Position:"      [Incremental.text (pos   |> Mod.map  (fun d -> d))]
            Html.row "PrintPosition:" [button [clazz "ui button tiny"; onClick (fun _ -> PrintPosition )][]]
            Html.row "Height:"        [Incremental.text (height  |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "HeightDelta:"   [Incremental.text (heightD |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "Avg Altitude:"  [Incremental.text (alt     |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "Length:"        [Incremental.text (length  |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "WayLength:"     [Incremental.text (wLength |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "Bearing:"       [Incremental.text (bearing |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "Slope:"         [Incremental.text (slope   |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "Vertical Distance:"   [Incremental.text (vertDist  |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
            Html.row "Horizontal Distance:" [Incremental.text (horDist   |> Mod.map  (fun d -> sprintf "%.4f" (d)))]
          ]
        )
       
    let app = 
        {
            unpersist = Unpersist.instance
            threads   = fun _ -> ThreadPool.empty
            initial   = Annotation.initial
            update    = update
            view      = view
        }

    let start() = App.start app

