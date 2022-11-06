import { check } from 'k6'
import http from 'k6/http'

//负载测试
export const options = {
    vus: 10, // 指定并发运行的vu数量的数字
    duration: '1m', // 持续运行的时间
    insecureSkipTLSVerify: true //为true的时候将忽略为在服务器提供的TLS证书中建立信任而进行的所有验证
};

export default () => {
    const url = "http://localhost:5098/Home/AddUser";

    // 如果是post
    const payload = JSON.stringify({
        userName: "",
        password: "123456"
    });

    const params = {
        headers: {
            "Content-Type": "application/type"
        },
    };

    const res = http.post(url, payload, params);
    check(res, {
        "状态400": (r) => r.status === 400,
    })
}