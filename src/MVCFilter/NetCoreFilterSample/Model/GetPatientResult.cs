namespace NetCoreFilterSample.Model
{
    public class GetPatientResult
    {
        public string PatientId { get; set; }

        public string PatientName { get; set; }

        public string IdCard { get; set; }


        public GetPatientResult GetDetails()
        {
            return new GetPatientResult { IdCard = Guid.NewGuid().ToString(), PatientId = "11", PatientName = "名称11" };
        }

        public IEnumerable<GetPatientResult> GetList()
        {
            return new List<GetPatientResult>()
            {
                new GetPatientResult{  IdCard=Guid.NewGuid().ToString(), PatientId="11", PatientName="名称11"},
                  new GetPatientResult{  IdCard=Guid.NewGuid().ToString(), PatientId="22", PatientName="名称22"},
                    new GetPatientResult{  IdCard=Guid.NewGuid().ToString(), PatientId="33", PatientName="名称33"},
                      new GetPatientResult{  IdCard=Guid.NewGuid().ToString(), PatientId="44", PatientName="名称44"},
            };
        }
    }
}
