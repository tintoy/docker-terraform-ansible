#!/bin/bash

docker build -t tintoy/tfa-multicloud-demo -t tintoy/tfa-multicloud-demo:stable -f docker-build/DockerExecutorApi/Dockerfile .
docker build -t ddresearch/terraform-ansible-deploy docker-build/Deployer
