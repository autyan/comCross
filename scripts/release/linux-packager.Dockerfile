FROM ruby:3.3-bookworm

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
      ca-certificates \
      file \
      rpm \
      xz-utils \
    && gem install --no-document fpm \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /repo
ENTRYPOINT ["/bin/bash"]
