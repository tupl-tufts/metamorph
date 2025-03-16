FROM ubuntu:22.04

RUN apt-get update; \
  apt-get install -qy apt-transport-https && \
  apt-get update && \
  apt-get install -qy dotnet-sdk-6.0 dotnet-sdk-7.0 make unzip wget

# install python3
RUN apt-get update; \
  apt-get install -qy python3 python3-pip 

# install necessary python packages
RUN pip3 install matplotlib numpy psutil
ENV PATH="/metamorph/Metamorph/Binaries/z3/bin:$PATH"