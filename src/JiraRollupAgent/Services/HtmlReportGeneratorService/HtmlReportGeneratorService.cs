using System.Diagnostics.CodeAnalysis;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Extensions;
using Microsoft.Extensions.Configuration;

namespace JiraRollupAgent.Services.HtmlReportGeneratorService
{
    [ExcludeFromCodeCoverage]
    public class HtmlReportGeneratorService : IHtmlReportGeneratorService
    {
        //This is required to enable logging for this class.
        private readonly Serilog.ILogger _log = Serilog.Log.ForContext<HtmlReportGeneratorService>();
        private readonly IConfiguration _config;
        private readonly IUnitOfWork _unitOfWork;

        public HtmlReportGeneratorService(IConfiguration config, IUnitOfWork unitOfWork)
        {
            _config = config;
            _unitOfWork = unitOfWork;
        }

        #region Run

        public async Task<bool> Run()
        {
            var runStage = _config.GetValue<bool>("AppSettings:RunReportGeneration");

            try
            {
                if (runStage)
                {
                    _log.Here().Information("Starting process to generate the HTML rollup report...");

                    // TODO: read Initiatives (ordered by PriorityRank) with their BusinessSummary and
                    // nested Epic EngineeringSummaries via _unitOfWork, render to a single HTML report.
                    // Not yet implemented.
                    await Task.CompletedTask;

                    _log.Here().Information("Process to generate the HTML rollup report ** NOT YET IMPLEMENTED **.");
                    return true;
                }
                else
                {
                    _log.Here().Information("Process to generate the HTML rollup report completed ** SKIPPED **");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Process to generate the HTML rollup report ** FAILED! **");
                return false;
            }
        }

        #endregion
    }
}
