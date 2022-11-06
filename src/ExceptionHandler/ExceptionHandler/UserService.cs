using ExceptionHandler.Model;
using LanguageExt.Common;

namespace ExceptionHandler;

public class UserService : IUserService
{
    public Result<bool> Add(AddUser addUser)
    {
        if (string.IsNullOrWhiteSpace(addUser.UserName))
        {
            //throw new ArgumentNullException(nameof(addUser.UserName));

            var validationException = new ArgumentNullException("用户名不能为空");
            return new Result<bool>(validationException);
        }

        return true;
    }
}

public interface IUserService
{
    Result<bool> Add(AddUser addUser);
}