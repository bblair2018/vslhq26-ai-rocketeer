using System.Diagnostics.CodeAnalysis;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Extensions;
using Microsoft.Extensions.Configuration;

namespace JiraRollupAgent.Services.SummarizationService
{
    [ExcludeFromCodeCoverage]
    public class SummarizationService : ISummarizationService
    {
        //This is required to enable logging for this class.
        private readonly Serilog.ILogger _log = Serilog.Log.ForContext<SummarizationService>();
        private readonly IConfiguration _config;
        private readonly IUnitOfWork _unitOfWork;

        public SummarizationService(IConfiguration config, IUnitOfWork unitOfWork)
        {
            _config = config;
            _unitOfWork = unitOfWork;
        }

        #region Run

        public async Task<bool> Run()
        {
            var runStage = _config.GetValue<bool>("AppSettings:RunSummarization");

            try
            {
                if (runStage)
                {
                    _log.Here().Information("Starting process to generate item/Epic/Initiative summaries via Azure OpenAI...");

                    // TODO: item summarization (Story/Bug/Task/Spike) -> Epic Engineering Summary
                    // (Dev/QA weighted) -> Initiative Business Summary (ScrumMaster/Stakeholder/
                    // EngineeringManager weighted), persisted via _unitOfWork.ItemSummaries /
                    // EpicEngineeringSummaries / InitiativeBusinessSummaries. Not yet implemented.
                    await Task.CompletedTask;

                    _log.Here().Information("Process to generate item/Epic/Initiative summaries ** NOT YET IMPLEMENTED **.");
                    return true;
                }
                else
                {
                    _log.Here().Information("Process to generate item/Epic/Initiative summaries completed ** SKIPPED **");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Process to generate item/Epic/Initiative summaries ** FAILED! **");
                return false;
            }
        }

        #endregion
    }
}
