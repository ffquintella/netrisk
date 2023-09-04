using AutoMapper;
using DAL;
using Serilog;

namespace ServerServices.Services;

public class BaseService
{
    private DALManager _dalManager;
    protected ILogger _log;
    protected IMapper _mapper;
    
    public BaseService(
        ILogger logger, 
        DALManager dalManager,
        IMapper mapper
    )
    {
        _dalManager = dalManager;
        _log = logger;
        _mapper = mapper;
    }

    protected DALManager DALManager => _dalManager;
}