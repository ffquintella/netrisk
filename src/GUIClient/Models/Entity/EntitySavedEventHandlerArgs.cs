using System;

namespace GUIClient.Models.Entity;

public class EntitySavedEventHandlerArgs: EventArgs
{
    public DAL.Entities.Entity Entity { get; set; }
}