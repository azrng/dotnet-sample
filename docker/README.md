# Quack Test Container

This benchmark uses one shared Quack/DuckDB container for both clients. Do not start separate containers for `Quack.DuckDB` and `Azrng.DuckDB.Quack`; that would mix client performance with server state and container scheduling differences.

## Image

```text
registry.cn-hangzhou.aliyuncs.com/zrng/duckdb-quack:1.5.3.1
```

## Start

From the repository root:

```bash
docker compose -f docker/compose.yml up -d
docker compose -f docker/compose.yml ps
```

Default connection string:

```text
Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true
```

Stop:

```bash
docker compose -f docker/compose.yml down
```

If another container already maps port `9494`, stop it first or change the host port in `compose.yml` and update `QUACK_PROTOCOL_CONNECTION_STRING`.

The container persists data under `${PWD}/data/duckdb` and joins the `my-bridge` Docker network.

Resource limits:

- CPU: 2 cores
- Memory: 4 GB
- Swap: disabled by setting `memswap_limit` equal to `mem_limit`
