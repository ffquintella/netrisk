using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.EntitiesDto;

namespace ClientServices.Interfaces;

public interface IReportTemplatesService
{
    Task<List<ReportTemplate>> GetAllAsync();
    Task<ReportTemplate> GetByIdAsync(int id);
    Task<ReportTemplate> CreateAsync(ReportTemplateCreateDto template);
    Task<ReportTemplate> UpdateAsync(int id, ReportTemplateUpdateDto template);
    Task DeleteAsync(int id);
}
