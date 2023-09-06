using System;

namespace GUIClient.Models.Events;

public class EntitySavedEventHandlerArgs: EventArgs
{
    public DAL.Entities.Entity Entity { get; set; }
}