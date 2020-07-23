using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Transactions;
using InspectionCenterService.Domain.Configuration;
using InspectionCenterService.Domain.Constants;
using InspectionCenterService.Domain.Contracts.Domain;
using InspectionCenterService.Domain.Contracts.Repositories;
using InspectionCenterService.Domain.Dto;
using InspectionCenterService.Domain.Entities;
using InspectionProcessService.Domain.Contracts.Remote;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlatformCommons.PlatformService.Abstractions.ServiceInquiry;
using PlatformCommons.PlatformService.Abstractions.ServiceLookup.Dto;
using PlatformCommons.Service.Domain;
using PlatformCommons.Service.Domain.Entities;
using PlatformCommons.Service.Domain.Exceptions;
using PlatformCommons.Service.Domain.Interfaces;
using PlatformCommons.Service.Domain.Utils;

namespace InspectionCenterService.Domain
{
    public class InspectionCenterManager : IInspectionCenterManager
    {
        #region Fields

        private readonly IServiceInquiry _serviceInquiry;
        private readonly ILookupService _lookupService;
        private readonly IInspectionCenterRepository _centerRepository;
        private readonly IInspectionCenterCoverageRepository _coverageRepository;
        private readonly IRoleEmailRepository _roleEmailsRepository;
        private readonly IRefitServiceResolver _refitResolver;
        private readonly CustomServiceSettings _customServiceSettings;
        private readonly IStringLocalizer _l;
        private readonly MessagePublisherSettings _messagePublisherConfigSettings;
        private readonly IMessageQueueManager _messageQueueManager;


        #endregion

        #region Constructor

        public InspectionCenterManager(
            IServiceInquiry serviceInquiry,
            ILookupService lookupService,
            IInspectionCenterRepository inspectionCenterRepository,
            IInspectionCenterCoverageRepository inspectionCenterCoverageRepository,
            IRoleEmailRepository roleEmailRepository,
            IOptions<CustomServiceSettings> customServiceSettings,
            IRefitServiceResolver refitResolver,
            IStringLocalizer l,
            IOptions<MessagePublisherSettings> messagePublisherConfigSettings,
            IMessageQueueManager messageQueueManager
        )
        {
            _serviceInquiry = serviceInquiry;
            _lookupService = lookupService;
            _centerRepository = inspectionCenterRepository;
            _coverageRepository = inspectionCenterCoverageRepository;
            _roleEmailsRepository = roleEmailRepository;
            _refitResolver = refitResolver;
            _customServiceSettings = customServiceSettings.Value;
            _l = l;
            _messagePublisherConfigSettings = messagePublisherConfigSettings.Value;
            _messageQueueManager = messageQueueManager;
        }

        #endregion

        #region Members

        private Dictionary<string, object> GetCustomProperties(string requestBody, string entityName)
        {
            var properties = _serviceInquiry.GetSchemaCustomColumnsByCode(entityName).Result;

            return properties?.Count > 0 ? ServiceDomainEntityHelper.PopulateCustomProperties(requestBody, properties) : null;
        }

        private List<InspectionCenterCoverage> GetCoverageByCenterID(Guid Id)
        {
            return _coverageRepository.GetCoverageByCenterId(Id, false);
        }

        private List<InspectionCenterCoverage> GetAllCoverageExceptCurrent(Guid Id)
        {
            return _coverageRepository.GetCoverageByCenterId(Id, true);
        }

        private IEnumerable<JToken> GetInspectionCoverages(string requestBody)
        {
            var request = JObject.Parse(requestBody);
            var coverageToken = request.SelectTokens("$.inspectionCenterCoverages");

            // At least one city & distict selected ..
            if (coverageToken.Children().Count() == 0)
            {
                throw new ServiceBaseException(_l["At least one inspection area should be selected"], 1000);
            }

            return coverageToken;
        }

        public async Task<List<LookupItemDto>> GetCoverageStreet(string centerId)
        {
            var streetLookup = new List<LookupItemDto>();
            var inspectionCenter = Get(new Guid(centerId)) as IDictionary<string, object>;

            if (inspectionCenter.ContainsKey("InspectionCenterCoverages"))
            {
                object centerCoverage = null;
                inspectionCenter.TryGetValue("InspectionCenterCoverages", out centerCoverage);

                var cityIds = new List<int>();
                foreach (var coverageItem in (dynamic)centerCoverage)
                {
                    var item = (IDictionary<string, object>)coverageItem;
                    if (item.ContainsKey("CityId"))
                    {
                        cityIds.Add((int)item["CityId"]);
                    }
                }

                var cities = await _lookupService.GetCities(new CityFilter { Ids = cityIds });
                if (cities != null && cities.Count > 0)
                {
                    foreach (var city in cities)
                    {
                        var Criteria = new Dictionary<string, string> { };
                        Criteria["cityCode"] = city.CityCode;

                        var streets = await _lookupService.GetStreets(new LookupFilterDto { Criteria = Criteria });
                        streetLookup.AddRange(streets.Values);
                    }
                }
            }

            return streetLookup;
        }

        public async Task<List<LookupItemDto>> GetCoverageStreet(IEnumerable<string> centerIds)
        {
            var streetLookup = new List<LookupItemDto>();
            foreach (var centerId in centerIds)
            {
                var coverages = await GetCoverageStreet(centerId);
                streetLookup.AddRange(coverages);
            }

            return streetLookup;
        }

        private List<RoleEmails> GetRoleEmailsByCenterId(Guid Id)
        {
            return _roleEmailsRepository.GetRoleEmailsByCenterId(Id);
        }

        private IEnumerable<JToken> GetRoleEmails(string requestBody)
        {
            var request = JObject.Parse(requestBody);
            var coverageToken = request.SelectTokens("$.roleEmails");

            // At least one role & roleEmail selected ..
            if (coverageToken.Children().Count() == 0)
            {
                throw new ServiceBaseException(_l["At least one role email should be added"], 1000);
            }

            return coverageToken;
        }

        /// <summary>
        /// Add Role Emails List
        /// </summary>
        private void AddRoleEmailsList(InspectionCenter inspectionCenter, string requestBody,Dictionary<string,object> roleEmailCustomProperties)
        {
            var groupEmailsToken = GetRoleEmails(requestBody);
            // Get DB update Role Emails
            //dbRoleEmails.CopyTo(dbUpdateRoleEmails);

            foreach (var item in groupEmailsToken.Children())
            {
                var groupEmails = item.ToObject<RoleEmails>();

                //check exists role with new email
                if (groupEmailsToken.Children().Values("role").Where(role => role.ToString() == groupEmails.Role).Count() > 1)
                    throw new ServiceBaseException(_l["Cannot repeat role inside inspection center:"], 1000);

                AddRoleEmails(inspectionCenter, item, roleEmailCustomProperties);
            }
        }

        /// <summary>
        /// Add Role Emails
        /// </summary>
        private void AddRoleEmails(InspectionCenter inspectionCenter, JToken item, Dictionary<string, object> customProperties)
        {
            var groupEmailsItem = item.ToObject<RoleEmails>();

            groupEmailsItem.Id = Guid.NewGuid();
            groupEmailsItem.InspectionCenterId = inspectionCenter.Id;
            groupEmailsItem.CreationTime = DateTime.Now;

            _roleEmailsRepository.Add(groupEmailsItem, customProperties);
        }

       

        /// <summary>
        /// Add Inspection Center Coverage
        /// </summary>
        private void AddCoverage(InspectionCenter inspectionCenter, JToken item, Dictionary<string, object> customProperties)
        {
            var inspectionCenterCoverageItem = item.ToObject<InspectionCenterCoverage>();

            // Check IF District Already Assigned to Another Inspection Center
            //IsOtherCoverageExists(inspectionCenter, inspectionCenterCoverageItem);
            inspectionCenterCoverageItem.InspectionCenterId = inspectionCenter.Id;
            inspectionCenterCoverageItem.CreationTime = DateTime.Now;
            _coverageRepository.Add(inspectionCenterCoverageItem, customProperties);
        }

        private void IsOtherCoverageExists(List<InspectionCenterCoverage> coverage)
        {
            var containsDuplicateDistrict = coverage.Select(x => x.DistrictId).Distinct().Count() < coverage.Count();
            if (containsDuplicateDistrict)
            {
                throw new ServiceBaseException(_l["selected coverage area repeated with another center or within the same center"], 1000);
            }
            // This needs to be finalized as how we can check if the coverage is covered by another inspection center if this is in same table.
            //var otherCoverage = GetAllCoverageExceptCurrent(center.Id);

            //if (otherCoverage.Exists(_cov => _cov.CityId == coverage.CityId && _cov.DistrictId == coverage.DistrictId) ||
            //    otherCoverage.Exists(_cov => _cov.CityId == coverage.CityId && _cov.DistrictId == InspectionCenterConstants.AllDistrict))
            //{
            //    throw new ServiceBaseException(_l["Inspection region covered by another inspection center"], 1000);
            //}
        }

        public ExpandoObject Get(Guid id)
        {
            // Inspection Center
            var customSchemaProperties = _serviceInquiry.GetSchemaCustomColumnsByCode(InspectionCenterConstants.InspectionCenterEntity).Result;
            dynamic inspectionCenterObject = _centerRepository.Get(new InspectionCenter() { Id = id }, customSchemaProperties.Select(prop => prop.Name.ToLower()).ToList());

            var s = JsonConvert.SerializeObject(inspectionCenterObject);
            var result = JsonConvert.DeserializeObject<ExpandoObject>(s, new QaiserCustomType2());

            return result;
        }

        //private object PopulateNewInspectionTarget<T>(string json, string type)
        //{
        //    try
        //    {
        //        var newTargetschema = _serviceEntitySchemaManager.GetSchemaByCode(type).Result;
        //        return ServiceDomainEntityHelper.PopulateExtendedEntity<T>(json, newTargetschema.EntitySchemaOutput.ConfigurationJSON, type, _serviceSettings.SchemaName);
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Logger.Error(ex, ex.Message);
        //        throw new UnhandledException(_localizer["General Exception"], 1000);
        //    }
        //}

        public IEnumerable<InspectionCenter> GetAll()
        {
            return _centerRepository.GetAll();
        }

        public PageResult<InspectionCenter> Find(string name, PageQuery pageQuery = null)
        {
            return _centerRepository.Find(name, pageQuery);
        }

        private bool IfNotExists(InspectionCenter inspectionCenter)
        {
            var inspectionCenterResult = _centerRepository.CheckIfAlreadyExists(inspectionCenter);

            if (inspectionCenterResult == null)
            {
                return true;   
            }
            throw new ServiceBaseException(_l["Cannot repeat inspection center with the same name"], 1000);
        }

        public bool Update(InspectionCenter inspectionCenter, string requestBody)
        {
            ValidateInputPattern(inspectionCenter);

            // Check IF Inspection Center Already Present
            if (!IfNotExists(inspectionCenter))
                throw new ServiceBaseException(_l[InspectionCenterConstants.NO_DATA_FOUND]);

            // Dyanmic Custom Controls
            var centerCustomProperties = GetCustomProperties(requestBody, InspectionCenterConstants.InspectionCenterEntity);

            // Inspection Center Coverage
            // Get DB Inspection Center Coverage
            var dbInspectionCenterCoverage = GetCoverageByCenterID(inspectionCenter.Id);

            var inspectionCenterCoverageToken = GetInspectionCoverages(requestBody);

            using (TransactionScope scope = new TransactionScope())
            {

                inspectionCenter.LastModificationTime = DateTime.Now;

               

                foreach (var item in inspectionCenterCoverageToken.Children())
                {
                  //  var coverage = item.ToObject<InspectionCenterCoverage>();
                   // if (dbInspectionCenterCoverage.Exists(predicate => predicate.Id == coverage.Id))
                   // {
                             //   IsOtherCoverageExists(inspectionCenter, coverage);

                        //        coverage.InspectionCenterId = inspectionCenter.Id;
                        //        coverage.LastModificationTime = DateTime.Now;

                        //       // _coverageRepository.Update(coverage, centerCoverageCustomProperties);

                        //        // Remove Items In Memory DB List
                        //        dbInspectionCenterCoverage.RemoveAll(predicate => predicate.Id == coverage.Id);
                        //    }
                        //    else
                        //    {
                        //        //AddCoverage(inspectionCenter, item, centerCoverageCustomProperties);
                    //}
                }

                //foreach (var coverage in dbInspectionCenterCoverage)
                //{
                //    _coverageRepository.Delete(coverage);
                //}

                // Role Emails
                // Get DB Role Emails
                //var dbRoleEmails = GetRoleEmailsByCenterId(inspectionCenter.Id);
                //var dbUpdateRoleEmails = new List<RoleEmails>(dbRoleEmails);


                //foreach (var item in roleEmailsToken.Children())
                //{
                //    var groupEmails = item.ToObject<RoleEmails>();

                //    //check exists role with new email
                //    if (roleEmailsToken.Children().Values("role").Where(role => role.ToString() == groupEmails.Role).Count() > 1)
                //        throw new ServiceBaseException(_l["Cannot repeat role inside inspection center:"], 1000);

                //    if (dbRoleEmails.Exists(predicate => predicate.Id == groupEmails.Id))
                //    {
                //        groupEmails.InspectionCenterId = inspectionCenter.Id;
                //        groupEmails.LastModificationTime = DateTime.Now;

                //        _roleEmailsRepository.Update(groupEmails, roleEmailCustomProperties);

                //        // Remove Items In Memory DB List
                //        dbRoleEmails.RemoveAll(predicate => predicate.Id == groupEmails.Id);
                //    }
                //    else
                //    {
                //        //check exists role with new email
                //        if (dbUpdateRoleEmails.Exists(current => current.Role == groupEmails.Role))
                //            throw new ServiceBaseException(_l["Cannot repeat role inside inspection center:"], 1000);

                //        AddRoleEmails(inspectionCenter, item, roleEmailCustomProperties);
                //    }
                //}
                _centerRepository.Update(inspectionCenter, centerCustomProperties);
                _messageQueueManager.SendUpdateCenter(_messagePublisherConfigSettings.CenterUpdatedExchange, new UpdatedCenter { CenterId = inspectionCenter.Id });

                scope.Complete();
            }
            return true;
        }

        public bool Create(InspectionCenter inspectionCenter, string requestBody)
        {

            ValidateInputPattern(inspectionCenter);
            // Check IF Inspection Center Already Present
            IfNotExists(inspectionCenter);

            // Dyanmic Custom Controls
            var customProperties = GetCustomProperties(requestBody, InspectionCenterConstants.InspectionCenterEntity);

            //var inspectionCenterCoverageToken = GetInspectionCoverages(requestBody);
            //var inspectionCenterCoverageItem = inspectionCenterCoverageToken.Children().FirstOrDefault();
            //var centerCoverageCustomProperties = GetCustomProperties(inspectionCenterCoverageItem.ToString(),
            //    InspectionCenterConstants.InspectionCenterCoverageEntity);

            //var roleEmailsToken = GetRoleEmails(requestBody);

            //RoleEmail Custom Property
            //var roleEmailCustomProperties =
            //    GetCustomProperties(roleEmailsToken.Children().FirstOrDefault().ToString(),
            //        InspectionCenterConstants.RoleEmailsEntity);

            using (TransactionScope scope = new TransactionScope())
            {
               
                inspectionCenter.Id = Guid.NewGuid();
                inspectionCenter.CreationTime = DateTime.Now;
               // inspectionCenter.RoleEmails = inspectionCenter.RoleEmails.ToString();
                _centerRepository.Add(inspectionCenter, customProperties);

                //foreach (var item in inspectionCenterCoverageToken.Children())
                //{
                //    AddCoverage(inspectionCenter, item, centerCoverageCustomProperties);
                //}

                // Role Emails
                //AddRoleEmailsList(inspectionCenter, requestBody,roleEmailCustomProperties);

                _messageQueueManager.SendUpdateCenter(_messagePublisherConfigSettings.CenterUpdatedExchange, new UpdatedCenter { CenterId = inspectionCenter.Id });

                scope.Complete();
            }
            return true;
        }

        public IList<InspectionCenter> GetAllInspectionCentersWithCoverages()
        {
            List<InspectionCenter> inspectionCenters = new List<InspectionCenter>();
            List<InspectionCenterCoverage> inspectionCenterCoverages = new List<InspectionCenterCoverage>();
            inspectionCenters = _centerRepository.GetAll().ToList();
            inspectionCenterCoverages = _coverageRepository.GetAll().ToList();
            foreach (var center in inspectionCenters)
            {
                //center.InspectionCenterCoverages = inspectionCenterCoverages.Where(_x => _x.InspectionCenterId == center.Id).ToList(); ;
            }
            return inspectionCenters;

        }
        private void ValidateInputPattern(InspectionCenter inspectionCenter)
        {
            if (string.IsNullOrWhiteSpace(inspectionCenter.Name) || !Regex.IsMatch(inspectionCenter.Name, @"^[ء-يa-zA-Z0-9-&-_@ ]{1,150}$"))
            {
                throw new ServiceBaseException(_l["Enter valid value for Center name"], 020);
            }
            if (string.IsNullOrWhiteSpace(inspectionCenter.RefCode) || !Regex.IsMatch(inspectionCenter.RefCode, @"^[a-zA-Z0-9]{1,150}$"))
            {
                throw new ServiceBaseException(_l["Enter valid value for RefCode"], 020);
            }
           
        }

        public InspectionCenter GetCenterByRefCode(string refCode)
        {
            var inspectionCenter = _centerRepository.GetByRefCode(refCode);
            return inspectionCenter;
        }

        #endregion
    }
}
