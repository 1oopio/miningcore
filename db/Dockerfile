FROM alpine:3.12 AS build-minicore

RUN apk --update --no-cache add git
RUN git clone -b dev https://github.com/JJPool/miningcore /miningcore

FROM postgres:14.2-alpine3.15

RUN mkdir /miningcore
COPY --from=build-minicore /miningcore/src/Miningcore/Persistence/Postgres/Scripts/ /miningcore
COPY --chmod=775 ./init-user-db.sh /docker-entrypoint-initdb.d/