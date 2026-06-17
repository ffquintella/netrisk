using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.EntitiesDto;

namespace ClientServices.Interfaces
{
    public interface IReportSchedulesService
    {
        Task<List<ReportSchedule>> GetAllAsync();
        Task<ReportSchedule> GetByIdAsync(int id);
        Task<ReportSchedule> CreateAsync(ReportScheduleCreateDto schedule);
        Task<ReportSchedule> UpdateAsync(int id, ReportScheduleUpdateDto schedule);
        Task DeleteAsync(int id);
        Task TriggerTestAsync(int id);
    }
}
