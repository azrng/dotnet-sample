namespace RazorLightWebApp.Models;

public class UserGradeDto
{
    public string UserName { get; set; } = null!;

    /// <summary>
    /// 科目成绩集合
    /// </summary>
    public IEnumerable<SubjectGradeDto> SubjectList { get; set; } = null!;

    public static UserGradeDto GetInfo()
    {
        return new UserGradeDto
        {
            UserName = "张三",
            SubjectList = new List<SubjectGradeDto>
            {
                new SubjectGradeDto
                {
                    SubjectName = "语文",
                    Grade = 90
                },
                new SubjectGradeDto
                {
                    SubjectName = "数学",
                    Grade = 80
                },
                new SubjectGradeDto
                {
                    SubjectName = "英语",
                    Grade = 70
                }
            }
        };
    }
}

public class SubjectGradeDto
{
    /// <summary>
    /// 科目名字
    /// </summary>
    public string SubjectName { get; set; } = null!;

    /// <summary>
    /// 成绩
    /// </summary>
    public int Grade { get; set; }
}