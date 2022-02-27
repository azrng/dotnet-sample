namespace NetCoreFilterSample.Model
{
    /// <summary>
    /// 添加患者吃饭请求类
    /// </summary>
    public class AddPatientEatRequest
    {
        /// <summary>
        /// 患者ID
        /// </summary>
        public int PatientId { get; set; }

        /// <summary>
        /// 饭
        /// </summary>
        public string Eat { get; set; }
    }
}
