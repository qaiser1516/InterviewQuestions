using AutoMapper;
using InspectionCenterService.Application.Dto;
using InspectionCenterService.Domain.Contracts.Domain;
using InspectionCenterService.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using PlatformCommons.Service.Domain.Entities;
using PlatformCommons.Service.Domain.Interfaces;
using PlatformCommons.Service.Domain.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InspectionCenterService.Application.Controllers
{
    [Produces("application/json")]
    [Route("api/inspection-centers")]
    [Authorize]
    public class InspectionCenterController : Controller
    {
        private readonly IInspectionCenterManager _inspectionCenterManager;
        private readonly IMapper _mapper;
        private readonly ICurrentUserClaims _currentUserClaims;

        public InspectionCenterController(IInspectionCenterManager inspectionCenterManager, IMapper mapper, ICurrentUserClaims currentUserClaims)
        {
            _inspectionCenterManager = inspectionCenterManager;
            _mapper = mapper;
            _currentUserClaims = currentUserClaims;
        }

        [HttpGet]
        public ActionResult GetAll()
        {
            return Ok(_inspectionCenterManager.GetAll());
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            var inspectionCenter = _inspectionCenterManager.Get(id) as IDictionary<string, object>;
            var serializedResult = JsonConvert.SerializeObject(inspectionCenter, Formatting.None, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            return Ok(serializedResult);
        }

        [HttpGet("getbyid/{id}")]
        public ActionResult GetById(Guid id)
        {
            var inspectionCenter = _inspectionCenterManager.Get(id) as IDictionary<string, object>;
            return Ok(inspectionCenter);
        }

        [HttpGet("center-coverage")]
        public async Task<ActionResult> GetCenterCoverage()
        {
            var currentCenters = _currentUserClaims.GetUserCenterIds();
            if (currentCenters?.Count() > 0)
            {
                var coverageStreetLookup = await _inspectionCenterManager.GetCoverageStreet(currentCenters);
                return Ok(JsonUtils<object>.Serialize(coverageStreetLookup));
            }
            else
            {
                return BadRequest(new { result = "failed" });
            }
        }
        [HttpGet("center-coverage/{centerId}")]
        public async Task<ActionResult> GetCenterCoverageByCenterId(string centerId)
        {
            var coverageStreetLookup = await _inspectionCenterManager.GetCoverageStreet(centerId);
            
            return Ok(coverageStreetLookup);
        }
        [HttpGet("getbyrefcode/{refCode}")]
        public ActionResult GetCenterByRefCode(string refCode)
        {
            var inspectionCenter = _inspectionCenterManager.GetCenterByRefCode(refCode);

            return Ok(inspectionCenter);
        }


        [HttpPost("search")]
        public ActionResult Find([FromBody] SearchFilterDto inspectionCenterDto)
        {
            return Ok(_inspectionCenterManager.Find(inspectionCenterDto.Name, (PageQuery)inspectionCenterDto));
        }

        [HttpPost]
        public ActionResult Add([FromBody] SubmissionDto submissionDto)
        {
            var typeObject = JsonConvert.DeserializeObject<InspectionCenterDto>(submissionDto.SubmissionJSON, new QaiserCustomType2());
            var inspectionCenterDto = JsonUtils<InspectionCenterDto>.Deserialize(submissionDto.SubmissionJSON);
             var inspectionCenter = _mapper.Map<InspectionCenter>(typeObject);
            
            _inspectionCenterManager.Create(inspectionCenter, submissionDto.SubmissionJSON);

            return Ok(new { result = "success" });
        }

        [HttpPost("update")]
        public ActionResult Update([FromBody] SubmissionDto submissionDto)
        {
            var inspectionCenterDto = JsonUtils<InspectionCenterDto>.Deserialize(submissionDto.SubmissionJSON);
            var inspectionCenter = _mapper.Map<InspectionCenter>(inspectionCenterDto);
            _inspectionCenterManager.Update(inspectionCenter, submissionDto.SubmissionJSON);

            return Ok(new { result = "success" });
        }

        [HttpPost]
        [Route("all-inspection-centers")]
        public ActionResult AllInspectionCentersWithCoverage()
        {

            return Ok(_inspectionCenterManager.GetAllInspectionCentersWithCoverages());
        }
    }
}