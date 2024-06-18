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
using System.Reactive;
using Avalonia.Layout;
using DynamicData;
using DynamicData.Binding;
using GUIClient.Models.Entity;
using GUIClient.Models.Events;
using Material.Icons;
using Material.Icons.Avalonia;
using Model.Exceptions;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace GUIClient.Views;

public partial class EntityForm : UserControl, IValidatableViewModel
{

    public IValidationContext ValidationContext { get; } = new ValidationContext();
    
    protected IStringLocalizer _localizer;
    public IStringLocalizer Localizer
    {
        get => _localizer;
        set => _localizer = value;
    }
    
    private IEntitiesService _entitiesService;
    
    private string? StrAvailable { get; set; } 
    private string? StrSelected { get; set; }
    
    public List<Object?> ControlValues { get; set; }
    
    public List<int> ControlIds { get; set; }
    
    public ReactiveCommand<Tuple<Entity, EntityDefinition>, Unit> BtSaveClicked { get; }
    
    private Entity? _entity;
    //public event EventHandler EntitySaved;
    public event EventHandler<EntitySavedEventHandlerArgs>? EntitySaved;
    
    public EntityForm(Entity entity, EntitiesConfiguration configuration): this()
    {
        _entity = entity;
        var definition = configuration.Definitions[entity.DefinitionName];
        CreateForm(entity, definition);
    }
    
    protected virtual void OnEntitySaved(EntitySavedEventHandlerArgs e)
    {
        EventHandler<EntitySavedEventHandlerArgs>? handler = EntitySaved;
        if (handler != null)
        {
            handler(this, e);
        }
    }
    
    private void ExecuteSave(Tuple<Entity, EntityDefinition> parameters)
    {
        var entity = parameters.Item1;
        var definition = parameters.Item2;
        
        var entityDto = new EntityDto();
        
        entityDto.Id = entity.Id;
        entityDto.DefinitionName = entity.DefinitionName;
        entityDto.EntitiesProperties = new List<EntitiesPropertyDto>();
        entityDto.Status = entity.Status;
        entityDto.Parent = entity.Parent;

        var idx = 0;
        foreach (var etype in definition.Properties)
        {
            switch (etype.Value.Type)
            {
                case "String":
                    var valuestr = (string?) ControlValues[idx];
                    if (valuestr != null )
                    {
                        entityDto.EntitiesProperties.Add(new EntitiesPropertyDto()
                        {
                            Id = ControlIds[idx],
                            Name = etype.Key + "-" + entity.Id,
                            Type = etype.Key,
                            Value = valuestr
                        });
                    }
                    break;
                case "Integer":
                    var valueDec = (Decimal?)ControlValues[idx];

                    if(valueDec == null) valueDec = 0;
                        
                    var valueInt = Convert.ToUInt32(valueDec);
                    
                    entityDto.EntitiesProperties.Add(new EntitiesPropertyDto()
                    {
                        Id = ControlIds[idx],
                        Name = etype.Key + "-" + entity.Id,
                        Type = etype.Key,
                        Value = valueInt.ToString()!
                    });
                    
                    
                    break;
                case "Boolean":
                    var valueBool = (bool?)ControlValues[idx];
                    if (valueBool != null )
                    {
                        entityDto.EntitiesProperties.Add(new EntitiesPropertyDto()
                        {
                            Id = ControlIds[idx],
                            Name = etype.Key + "-" + entity.Id,
                            Type = etype.Key,
                            Value = valueBool.ToString()!.ToLower()
                        });
                    }
                    break;
                default:
                    if (etype.Value.Type.StartsWith("Definition"))
                    {
                        
                        if (ControlValues[idx] != null)
                        {
                            if (definition.Properties.ElementAt(idx).Value.Multiple)
                            {
                                // Multiple
                                var valueDef = (IEnumerable<SelectEntity>?)ControlValues[idx];
                                if (valueDef != null)
                                {
                                    foreach (var vald in valueDef)
                                    {
                                        entityDto.EntitiesProperties.Add(new EntitiesPropertyDto()
                                        {
                                            
                                            Id = ControlIds[idx],
                                            Name = etype.Key + "-" + entity.Id + "-" + vald.Key,
                                            Type = etype.Key,
                                            Value = vald.Key
                                        });
                                    }
                                }
                            }
                            else
                            {
                                // Single
                                var valueDef = (SelectEntity?)ControlValues[idx];
                                if (valueDef != null)
                                {
                                    entityDto.EntitiesProperties.Add(new EntitiesPropertyDto()
                                    {
                                        Id = ControlIds[idx],
                                        Name = etype.Key + "-" + entity.Id,
                                        Type = etype.Key,
                                        Value = valueDef.Key
                                    });
                                }

                            }
                        }

                        break;
                    }
                    break;

            }
            idx ++;
        }

        var result = _entitiesService.SaveEntity(entityDto);

        if (result != null)
        {
            _entity = result;
            OnEntitySaved(new EntitySavedEventHandlerArgs(){Entity = _entity});
            
            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["EntitySavedSuccessMSG"] ,
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            msgOk.ShowAsync();
        }

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

        var btSave = new Button();
        btSave.Command = BtSaveClicked;
        btSave.CommandParameter = new Tuple<Entity, EntityDefinition>(entity, definition);
        btSave.Margin = new Thickness(5);

        var sp = new StackPanel();
        sp.Orientation = Orientation.Horizontal;
        
        var mIcon = new MaterialIcon();
        mIcon.Kind = MaterialIconKind.ContentSave;
        mIcon.Margin = new Thickness(5, 0);
        sp.Children.Add(mIcon);
        
        var saveText = new TextBlock();
        saveText.Text = Localizer["Save"];
        saveText.Margin = new Thickness(5, 0);
        sp.Children.Add(saveText);
        
        btSave.Content = sp;
        
        form.Children.Add(btSave);
        

        this.Content = form;
    }

    private void CreateControl(ref StackPanel panel, EntityType type, List<EntitiesProperty> values, int idx)
    {
        var strLocValue = Localizer[type.Label];
        var strValue = strLocValue.Value;
        if( strLocValue.ResourceNotFound || strLocValue.Value == "" ) strValue = type.Label;
        
        if (type.Type != "Boolean" && !type.Type.StartsWith("Definition") )
        {
            
            var label = new TextBlock { Text = strValue };
            label.Margin = new Thickness(5, 0 , 0,0);
            panel.Children.Add(label);
        }
        
        if (values.Count > 0 ) ControlIds.Add(values.FirstOrDefault()!.Id);
        else ControlIds.Add(0);
        
        switch (type.Type)
        {
            case "String":
                var value = "";
                ControlValues.Add(value);
                
                var tb = new TextBox();
                if (type.DefaultValue == null) type.DefaultValue = "";
                tb.Text = values.Count > 0 ? values.First().Value : type.DefaultValue;
                tb.Margin = new Thickness(5);
                var text = tb.GetObservable(TextBlock.TextProperty);
                var tbError = new TextBlock();
                tbError.IsVisible = false;
                tbError.Text = Localizer["PleaseEnterAValueMSG"];
                tbError.Margin = new Thickness(5);
                
                text.Subscribe(value =>
                {

                    if (!type.Nullable)
                    {
                        if (string.IsNullOrEmpty(value))
                        {
                            tbError.IsVisible = true;
                        }
                        else
                        {
                            tbError.IsVisible = false;
                        }
                    }

                    ControlValues[idx] = value;
                });
                
                panel.Children.Add(tbError);

                panel.Children.Add(tb);
                
                break;
            case "Integer":
                Decimal vint = 0;
                ControlValues.Add(vint);

                var ci = new NumericUpDown();
                if (type.DefaultValue == null) type.DefaultValue = "0";
                ci.Value = decimal.Parse(values.Count > 0 ? values.First().Value : type.DefaultValue);
                ci.Margin = new Thickness(5);
                
                ci.ValueChanged += (sender, args) =>
                {
                    ControlValues[idx] = ci.Value;
                };
                
                
                panel.Children.Add(ci);
                break;
            case "Boolean":
                var vbool = false;
                ControlValues.Add(vbool);
                
                //if(values.Count > 0) ControlIds.Add(values.FirstOrDefault()!.Id);
                var cb = new CheckBox();
                cb.Content = strValue;
                if (type.DefaultValue == null) type.DefaultValue = "false";
                cb.IsChecked = values.Count > 0 ? bool.Parse(values.First().Value) : bool.Parse(type.DefaultValue);
                cb.Margin = new Thickness(5);

                cb.WhenAnyValue(x => x.IsChecked).Subscribe(x =>
                {
                    ControlValues[idx] = x;
                });
               
                
                panel.Children.Add(cb);
                break;
            default:
                if (type.Type.StartsWith("Definition"))
                {
                    var vent = new Object();
                    ControlValues.Add(vent);
                    
                     var definition = type.Type.Split("(")[1].Split(")")[0];
                     
                     var defnitionEntities = _entitiesService.GetAll(definition);

                     if (type.Multiple)
                     {
                         var selectedEntities = defnitionEntities.Where(e => values.Any(v => v.Value == e.Id.ToString())).ToList();
                         
                         var ms = new MultiSelect();
                         ms.Title = strValue;
                         ms.StrAvailable = StrAvailable;
                         ms.StrSelected = StrSelected;
                         ms.Height = 250;
                         ms.Margin = new Thickness(5);
                         
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
                         
                         ms.WhenAnyValue(x => x.SelectedItems).Subscribe(x =>
                         {
                             ControlValues[idx] = x;
                         });
                         
                         panel.Children.Add(ms);
                     }
                     else
                     {
                         var label = new TextBlock { Text = strValue };
                         label.Margin = new Thickness(5, 0 , 0,0);
                         panel.Children.Add(label);
                         var combo = new ComboBox();
                         combo.Margin = new Thickness(5);
                         combo.ItemTemplate = new FuncDataTemplate<SelectEntity?>((x, _) =>
                         {
                             var tb = new TextBlock();
                             tb.Text = x != null ? x.Label : "";
                             return tb;
                         });
                         
                         var items = new List<SelectEntity>();
                         foreach (var defnitionEntity in defnitionEntities)
                         {
                                items.Add(new SelectEntity(defnitionEntity.Id.ToString(), defnitionEntity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value));
                         }

                         combo.ItemsSource = items;

                         combo.WhenAnyValue(x => x.SelectedItem).Subscribe(x =>
                         {
                             ControlValues[idx] = x;
                         });
                         
                         if (values.Count > 0)
                         {
                             combo.SelectedItem = items.FirstOrDefault(i => i.Key == values.First().Value);
                         }
                            
                         
                         
                         
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
        ControlIds = new List<int>();
     
        BtSaveClicked = ReactiveCommand.Create<Tuple<Entity, EntityDefinition>>(ExecuteSave);
        
        var localizationService = GetService<ILocalizationService>();
        _entitiesService = GetService<IEntitiesService>();
        _localizer = localizationService.GetLocalizer(typeof(EntityForm).Assembly);
        
        StrAvailable = Localizer["Available"];
        StrSelected = Localizer["Selected"];
    }
}