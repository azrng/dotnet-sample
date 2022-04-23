using RazorEngine;
using RazorEngine.Templating;
using RazorEngineConsoleApp.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace RazorEngineConsoleApp
{
    public class Service
    {
        /// <summary>
        /// 模板usergrade1
        /// </summary>
        public void GetUserGrade1()
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "View", "usergrade1.cshtml");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("模板文件不存在");
                return;
            }
            //打开并且读取模板
            string template = File.ReadAllText(filePath);

            //一次编译多次使用
            {
                //modelType为null
                //添加模板
                Engine.Razor.AddTemplate("usergrade1", template);
                //编译模板
                Engine.Razor.Compile("usergrade1", modelType: null);
                //运行模板
                string str = Engine.Razor.Run("usergrade1", modelType: null, UserGradeDto.GetInfo());
                Console.WriteLine(str);

                //modelType不为null
                ////添加模板
                //Engine.Razor.AddTemplate("usergrade1", template);
                ////编译模板
                //Engine.Razor.Compile("usergrade1", typeof(UserGradeDto));
                ////运行模板
                //string str = Engine.Razor.Run("usergrade1", typeof(UserGradeDto), UserGradeDto.GetInfo());
                //Console.WriteLine(str);
            }

            //一次编译一次使用
            {
                //var str = Engine.Razor.RunCompile(template, "usergrade1", typeof(UserGradeDto), UserGradeDto.GetInfo());
            }
        }

        /// <summary>
        /// 模板usergrade2  根据dynamic方式去生成成功
        /// </summary>
        public void GetUserGrade2()
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "View", "usergrade2.cshtml");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("模板文件不存在");
                return;
            }
            //打开并且读取模板
            string template = File.ReadAllText(filePath);
            //dynamic方式
            var result = Engine.Razor.RunCompile(template, "templateKey", null, new { UserName = "李思", SubjectList = new List<SubjectGradeDto> { new SubjectGradeDto { SubjectName = "语文", Grade = 90 } } });
            Console.WriteLine(result);
        }

        /// <summary>
        /// 根据表结构生成类文件
        /// </summary>
        public void GetUserGrade3()
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "View", "modelTest.cshtml");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("模板文件不存在");
                return;
            }
            //打开并且读取模板
            string template = File.ReadAllText(filePath);

            //添加模板
            Engine.Razor.AddTemplate("modelTest", template);
            //编译模板
            Engine.Razor.Compile("modelTest", null);

            var data = new TableInfo
            {
                TableName = "userinfo",
                Desc = "用户信息表",
                Parameters = new List<DbParamInfo>
                 {
                     new DbParamInfo
                     {
                         ParamName="Id",
                         ParamType="int",
                          Desc="用户id"
                     },
                     new DbParamInfo
                     {
                         ParamName="Name",
                         ParamType="string",
                          Desc="用户名"
                     }
                 },
            };
            //运行模板并返回结果
            var result = Engine.Razor.Run("modelTest", null, data);
        }
    }
}