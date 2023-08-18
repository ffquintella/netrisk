using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using AvaloniaExtraControls.Models;
using AvaloniaExtraControls.MultiSelect;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using Model.Entities;
using ReactiveUI;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Extensions;
using Splat;

namespace GUIClient.Views;

public partial class EntityForm : UserControl, IValidatableViewModel
{

    public ValidationContext ValidationContext { get; } = new ValidationContext();
    
    protected IStringLocalizer? _localizer;
    public IStringLocalizer? Localizer
    {
        get => _localizer;
        set => _localizer = value;
    }
    
    private IEntitiesService _entitiesService;
    
    private string? StrAvailable { get; set; } 
    private string? StrSelected { get; set; }
    
    public List<Object?> ControlValues { get; set; }
    
    
    public EntityForm(Entity entity, EntitiesConfiguration configuration): this()
    {

        
        var definition = configuration.Definitions[entity.DefinitionName];
        CreateForm(entity, definition);
    }
    
    private void CreateForm(Entity entity, EntityDefinition definition)
    {
        var form = new StackPanel();
        int idx = 0;
        foreach (var (key, entityObj) in definition.Properties)
        {

            var values = entity.EntitiesProperties.Where(ep => ep.Type == key).ToList();
            
            CreateControl(ref form, entityObj, values, idx);

            idx++;

            //CreateControl(ref form, entityObj, entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == key)?.Value);
        }

        this.Content = form;
    }

    private void CreateControl(ref StackPanel panel, EntityType type, List<EntitiesProperty> values, int idx)
    {
        if (type.Type != "Boolean" && !type.Type.StartsWith("Definition") )
        {
            var label = new TextBlock { Text = type.Label };
            panel.Children.Add(label);
        }

        switch (type.Type)
        {
            case "String":
                var value = "";
                ControlValues.Add(value);
                
                var tb = new TextBox();
                if (type.DefaultValue == null) type.DefaultValue = "";
                tb.Text = values.Count > 0 ? values.First().Value : type.DefaultValue;
                var text = tb.GetObservable(TextBlock.TextProperty);

                text.Subscribe(value =>
                {
                    //TODO: FIX not nullable
                    /*if (!type.Nullable)
                    {
                        if (string.IsNullOrEmpty(value))
                        {
                            throw new ArgumentNullException(nameof(tb), "This field is required");
                        }
                    }*/

                    ControlValues[idx] = value;
                });
                
                panel.Children.Add(tb);

                    
                
                break;
            case "Integer":
                var ci = new NumericUpDown();
                if (type.DefaultValue == null) type.DefaultValue = "0";
                ci.Value = int.Parse(values.Count > 0 ? values.First().Value : type.DefaultValue);
                panel.Children.Add(ci);
                break;
            case "Boolean":
                var cb = new CheckBox();
                cb.Content = type.Label;
                if (type.DefaultValue == null) type.DefaultValue = "false";
                cb.IsChecked = values.Count > 0 ? bool.Parse(values.First().Value) : bool.Parse(type.DefaultValue);
                panel.Children.Add(cb);
                break;
            default:
                if (type.Type.StartsWith("Definition"))
                {
                     var definition = type.Type.Split("(")[1].Split(")")[0];
                     
                     var defnitionEntities = _entitiesService.GetAll(definition);

                     if (type.Multiple)
                     {
                         var selectedEntities = defnitionEntities.Where(e => values.Any(v => v.Value == e.Id.ToString())).ToList();
                         
                         var ms = new MultiSelect();
                         ms.Title = type.Label;
                         ms.StrAvailable = StrAvailable;
                         ms.StrSelected = StrSelected;
                         
                         var availableItems = new List<SelectEntity>();
                         var selctedItems = new List<SelectEntity>();
                         foreach (var defnitionEntity in defnitionEntities)
                         {
                                if (selectedEntities.Any(e => e.Id == defnitionEntity.Id))
                                {
                                    selctedItems.Add(new SelectEntity(defnitionEntity.Id.ToString(), defnitionEntity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value));
                                }
                                else
                                {
                                    availableItems.Add(new SelectEntity(defnitionEntity.Id.ToString(), defnitionEntity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value));
                                }
                         }

                         ms.AvailableItems = availableItems;
                         ms.SelectedItems = selctedItems;
                         
                         
                         panel.Children.Add(ms);
                     }
                     else
                     {
                         var label = new TextBlock { Text = type.Label };
                         panel.Children.Add(label);
                         var combo = new ComboBox();
                         combo.ItemTemplate = new FuncDataTemplate<SelectEntity>((x, _) =>
                         {
                             var tb = new TextBlock();
                             tb.Text = x.Label;
                             return tb;
                         });
                         
                         var items = new List<SelectEntity>();
                         foreach (var defnitionEntity in defnitionEntities)
                         {
                                items.Add(new SelectEntity(defnitionEntity.Id.ToString(), defnitionEntity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value));
                         }

                         combo.ItemsSource = items;
                         
                         if(values.Count > 0 )
                            combo.SelectedItem = items.FirstOrDefault(i => i.Key == values.First().Value);
                         
                         panel.Children.Add(combo);
                     }

                     break;
                }
                var textBox = new TextBox();
                panel.Children.Add(textBox);
                break;
            
        }

    }
    
    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
    
    public EntityForm()
    {
        ControlValues = new List<Object?>();
        
        var localizationService = GetService<ILocalizationService>();
        _entitiesService = GetService<IEntitiesService>();
        Localizer = localizationService.GetLocalizer(typeof(EntityForm).Assembly);
        
        StrAvailable = Localizer["Available"];
        StrSelected = Localizer["Selected"];
    }
}