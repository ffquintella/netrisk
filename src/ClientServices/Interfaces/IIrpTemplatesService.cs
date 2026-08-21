using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;

namespace ClientServices.Interfaces;

/// <summary>
/// Client access to IRP templates and their tasks (Track 2 milestone 2.4.1). Templates are the
/// versioned playbooks the automation engine instantiates when a matching incident is created.
/// </summary>
public interface IIrpTemplatesService
{
    Task<List<IrpTemplate>> GetAllAsync();

    Task<IrpTemplate> GetByIdAsync(int id);

    Task<IrpTemplate> CreateAsync(IrpTemplate template);

    Task<IrpTemplate> UpdateAsync(IrpTemplate template);

    Task DeleteAsync(int id);

    /// <summary>Duplicates a template and its task graph. The copy starts disabled.</summary>
    Task<IrpTemplate> CloneAsync(int id);

    Task<List<IrpTemplateTask>> GetTasksAsync(int templateId);

    /// <summary>
    /// Adds a task. The server rejects a predecessor that would close a dependency cycle.
    /// </summary>
    Task<IrpTemplateTask> CreateTaskAsync(int templateId, IrpTemplateTask task);

    Task<IrpTemplateTask> UpdateTaskAsync(int templateId, IrpTemplateTask task);

    Task DeleteTaskAsync(int templateId, int taskId);
}
